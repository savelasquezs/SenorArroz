using System.Text.Json;
using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Constants;

namespace SenorArroz.Application.Features.Orders.Commands;

/// <summary>
/// Moves an uncompleted order to another dispatch branch, optionally reassigning
/// ordinary bank payments. The existing mutation pipeline owns the transaction,
/// order lock, version, audit and post-commit notifications.
/// </summary>
public sealed class TransferOrderBranchHandler(
    IApplicationDbContext db,
    IOrderRepository orders,
    ICurrentUser currentUser,
    IBranchContext branchContext,
    IOrderMutationContext mutation,
    IOrderNotificationService notifications,
    IMapper mapper,
    IClock clock) : IRequestHandler<TransferOrderBranchCommand, OrderDto>
{
    public async Task<OrderDto> Handle(TransferOrderBranchCommand request, CancellationToken ct)
    {
        if (!Roles.IsSuperadmin(currentUser.Role))
            throw new BranchAccessDeniedException();

        if (mutation.Expected is not { } expected || expected.OrderId != request.Id)
            throw new OrderConcurrencyException();

        var order = await db.Orders.SingleOrDefaultAsync(
            o => o.Id == request.Id && o.TenantId == currentUser.TenantId, ct)
            ?? throw new NotFoundException("Pedido no encontrado");
        OrderAccessRules.EnsureAccess(order, currentUser, branchContext);

        if (order.Status is not (OrderStatus.AwaitingPayment or OrderStatus.Taken
            or OrderStatus.InPreparation or OrderStatus.Ready))
            throw new BusinessException("Solo se pueden cambiar de sucursal los pedidos pendientes, tomados, en preparación o listos.");

        var previousBranchId = order.BranchId;
        if (request.TargetBranchId <= 0 || request.TargetBranchId == previousBranchId)
            throw new BusinessException("Selecciona otra sucursal.");

        var targetExists = await db.Branches.AsNoTracking().AnyAsync(
            b => b.Id == request.TargetBranchId && b.TenantId == order.TenantId && b.IsActive, ct);
        if (!targetExists)
            throw new BusinessException("La sucursal de destino no existe o está inactiva.");

        var changes = request.BankPaymentChanges ?? [];
        if (changes.Any(x => x is null || x.PaymentId <= 0 || x.TargetBankId <= 0)
            || changes.Select(x => x.PaymentId).Distinct().Count() != changes.Count)
            throw new BusinessException("La selección de bancos contiene pagos duplicados o inválidos.");

        if (changes.Count > 0)
        {
            var paymentIds = changes.Select(x => x.PaymentId).ToArray();
            var targetBankIds = changes.Select(x => x.TargetBankId).Distinct().ToArray();
            var payments = await db.BankPayments.Include(p => p.Bank)
                .Where(p => p.TenantId == order.TenantId && p.OrderId == order.Id
                    && paymentIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, ct);
            if (payments.Count != paymentIds.Length)
                throw new BusinessException("Uno de los pagos seleccionados no pertenece a este pedido.");

            var targetBanks = await db.Banks.AsNoTracking()
                .Where(b => targetBankIds.Contains(b.Id) && b.TenantId == order.TenantId
                    && b.BranchId == request.TargetBranchId && b.Active && b.Type == BankType.Normal)
                .ToDictionaryAsync(b => b.Id, ct);
            if (targetBanks.Count != targetBankIds.Length)
                throw new BusinessException("Selecciona únicamente bancos activos y normales de la sucursal destino.");

            // Validate every payment before changing any. Closed financial periods
            // and linked reservation/app settlements must keep their original bank.
            foreach (var change in changes)
            {
                var payment = payments[change.PaymentId];
                if (payment.BankId == change.TargetBankId) continue;
                if (payment.SourceReservationDepositId.HasValue || payment.IsAppSettlement
                    || await db.ReservationDeposits.AsNoTracking().AnyAsync(d =>
                        d.OrderId == order.Id && !d.IsEffective && !d.AppId.HasValue
                        && d.BankId == payment.BankId && d.Amount == payment.Amount
                        && d.ReceivedAt <= payment.CreatedAt, ct))
                    throw new BusinessException(
                        "Los pagos vinculados a abonos de reserva o liquidaciones de apps conservan su banco original.");

                if (await db.CashRegisterClosures.AsNoTracking().AnyAsync(c =>
                    (c.BranchId == payment.Bank.BranchId || c.BranchId == request.TargetBranchId)
                    && c.ClosedAt >= payment.CreatedAt, ct))
                    throw new BusinessException(
                        "No se puede reasignar un pago contabilizado en un periodo con cierre de caja. Deja su banco original.");
            }

            foreach (var change in changes)
            {
                var payment = payments[change.PaymentId];
                if (payment.BankId == change.TargetBankId) continue;
                var oldBankId = payment.BankId;
                var oldVerified = payment.IsVerified;
                payment.BankId = change.TargetBankId;
                payment.IsVerified = false;
                payment.VerifiedAt = null;
                db.EntityAuditLogs.Add(new EntityAuditLog
                {
                    TenantId = order.TenantId, BranchId = request.TargetBranchId,
                    EntityType = "order_bank_reassignment", EntityId = payment.Id,
                    OperationType = "updated", ChangedByUserId = currentUser.Id,
                    ChangedAt = clock.UtcNow, BusinessDate = clock.UtcNow.AddHours(-5).Date,
                    SummaryText = $"Pedido #{order.Id}: pago #{payment.Id}, banco {oldBankId} a {payment.BankId}",
                    BeforeJson = JsonSerializer.Serialize(new { bankId = oldBankId, verified = oldVerified, payment.Amount }),
                    AfterJson = JsonSerializer.Serialize(new { bankId = payment.BankId, verified = false, payment.Amount }),
                    MetadataJson = JsonSerializer.Serialize(new { orderId = order.Id,
                        previousBranchId, targetBranchId = request.TargetBranchId, manualBankTransferRequired = true })
                });
            }
        }

        // An old kitchen voice event remains immutable and is ignored once its order moves.
        var visibleToKitchen = KitchenOrderNotificationEligibility.IsVisibleToActiveKitchen(order, clock.UtcNow);
        order.BranchId = request.TargetBranchId;
        await db.SaveChangesAsync(ct);

        var persisted = await orders.GetByIdWithFullDetailsAsync(order.Id, ct)
            ?? throw new NotFoundException("Pedido no encontrado");
        var result = mapper.Map<OrderDto>(persisted);

        await OrderPostCommitNotifications.Publish(mutation,
            () => notifications.NotifyOrderTransferred(order.Id, previousBranchId,
                request.TargetBranchId, visibleToKitchen));

        return result;
    }
}
