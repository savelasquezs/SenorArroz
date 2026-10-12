using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.Commands;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Application.Features.BankPayments.Commands;
using SenorArroz.Application.Features.AppPayments.Commands;
using SenorArroz.Application.Features.ReservationDeposits.Commands;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Application.Common.Behaviors;

/// <summary>
/// Serializes changes to an order and its payments. The current actor/branch is never impersonated.
/// Locks parents in ID order; authorization and the client's version are rechecked after locking.
/// Existing handlers remain responsible for role, state, amount, inventory and workflow rules.
/// </summary>
public sealed class OrderMutationBehavior<TRequest, TResponse>(
    IApplicationDbContext db,
    ICurrentUser user,
    IBranchContext branch,
    IOrderNotificationService notifications,
    ILogger<OrderMutationBehavior<TRequest, TResponse>> logger,
    IOrderMutationContext? mutation = null, IClock? clock = null) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return await next(ct);
        var ids = await ResolveOrderIds(request, ct);
        if (ids is null) return await next(ct);
        if (ids.Count == 0) throw new NotFoundException("Pedido o pago no encontrado");
        ids = ids.Distinct().OrderBy(id => id).ToList();
        var primaryIds = ids.ToList();
        ids = await AddSettlementParents(request, ids, ct);
        var before = await LoadAndAuthorize(ids, ct);
        var expected = mutation?.Expected;
        ValidateExpected(before, primaryIds, expected);

        await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct) : null;
        // PostgreSQL RLS still applies; explicit tenant predicate provides another boundary.
        if (db.Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) == true)
            foreach (var id in ids)
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT id FROM \"order\" WHERE id = {id} AND tenant_id = {user.TenantId} FOR UPDATE", ct);
        before = await LoadAndAuthorize(ids, ct);
        ValidateExpected(before, primaryIds, expected);

        var response = await next(ct);
        var changed = new List<(int Id, int Branch, int? Origin, long Version)>();
        foreach (var prior in before)
        {
            var current = await db.ReloadOrderForManagementAsync(prior.Id, ct);
            if (current is not null)
            {
                // Also increment for child-only changes (payments and deposits).
                current.ManagementVersion = checked(current.ManagementVersion + 1);
                await db.SaveChangesAsync(ct);
                if (response is OrderDto dto && dto.Id == current.Id)
                    dto.ManagementVersion = current.ManagementVersion;
                changed.Add((current.Id, current.BranchId, current.OriginBranchId, current.ManagementVersion));
                if (ids.Count == 1 || expected?.OrderId == current.Id)
                    mutation?.Complete(current.Id, current.ManagementVersion);
            }
            else changed.Add((prior.Id, prior.BranchId, prior.OriginBranchId, prior.ManagementVersion + 1));
        }
        var auditAt = clock?.UtcNow ?? DateTime.UtcNow;
        foreach (var change in changed)
            db.EntityAuditLogs.Add(new EntityAuditLog
            {
                TenantId = user.TenantId, BranchId = change.Branch, EntityType = "order_management",
                EntityId = change.Id, OperationType = "updated", ChangedAt = auditAt,
                BusinessDate = auditAt.AddHours(-5).Date, ChangedByUserId = user.Id,
                SummaryText = $"Pedido #{change.Id}: {typeof(TRequest).Name}",
                MetadataJson = JsonSerializer.Serialize(new
                {
                    command = typeof(TRequest).Name, workspaceBranchId = branch.EffectiveBranchId,
                    originBranchId = change.Origin, destinationBranchId = change.Branch,
                    managementVersion = change.Version
                })
            });
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        foreach (var change in changed)
        {
            try { await notifications.NotifyOrderManagementChanged(change.Id, change.Branch, change.Origin, change.Version); }
            catch (Exception ex) { logger.LogWarning(ex, "Order {OrderId} saved; realtime invalidation failed", change.Id); }
        }
        return response;
    }

    private async Task<List<Order>> LoadAndAuthorize(List<int> ids, CancellationToken ct)
    {
        var orders = await db.Orders.AsNoTracking().Where(o => ids.Contains(o.Id)).OrderBy(o => o.Id).ToListAsync(ct);
        if (orders.Count != ids.Count) throw new NotFoundException("Pedido no encontrado");
        foreach (var order in orders) OrderAccessRules.EnsureAccess(order, user, branch);
        return orders;
    }

    private void ValidateExpected(List<Order> orders, List<int> primaryIds, (int OrderId, long Version)? expected)
    {
        if (expected is { } e && (!primaryIds.Contains(e.OrderId) || !orders.Any(o => o.Id == e.OrderId && o.ManagementVersion == e.Version)))
            throw new OrderConcurrencyException();
        // Remote management must not silently overwrite a stale form, even from a hand-written request.
        if (orders.Any(o => OrderAccessRules.IsOriginAccess(o, user, branch)) && (expected is null || primaryIds.Count != 1))
            throw new OrderConcurrencyException();
    }

    private async Task<List<int>> AddSettlementParents(object request, List<int> ids, CancellationToken ct)
    {
        // A consolidated settlement can affect a parent other than the clicked payment's order.
        // Do not let authority over one order modify an unrelated order's settlement.
        int? bankId = request switch
        {
            UpdateBankPaymentCommand r => r.Id, DeleteBankPaymentCommand r => r.Id,
            VerifyBankPaymentCommand r => r.Id, UnverifyBankPaymentCommand r => r.Id, _ => null
        };
        var isUnsettle = request is UnsettleAppPaymentCommand;
        if (!bankId.HasValue && !isUnsettle) return ids;
        var settlements = bankId.HasValue
            ? await db.BankPayments.AsNoTracking().Where(p => p.Id == bankId && p.IsAppSettlement)
                .Select(p => new { p.OrderId, p.AppSettlementSourcePaymentIds }).ToListAsync(ct)
            : await db.BankPayments.AsNoTracking().Where(p => p.IsAppSettlement)
                .Select(p => new { p.OrderId, p.AppSettlementSourcePaymentIds }).ToListAsync(ct);
        foreach (var payment in settlements)
        {
            var sourceIds = AppSettlementBankPaymentSourceIds.Parse(payment.AppSettlementSourcePaymentIds);
            if (request is UnsettleAppPaymentCommand unsettle && !sourceIds.Contains(unsettle.Id)) continue;
            ids.Add(payment.OrderId);
            ids.AddRange(await db.AppPayments.AsNoTracking().Where(p => sourceIds.Contains(p.Id)).Select(p => p.OrderId).ToListAsync(ct));
        }
        return ids.Distinct().OrderBy(id => id).ToList();
    }

    private async Task<List<int>?> ResolveOrderIds(object request, CancellationToken ct)
    {
        int? orderId = request switch
        {
            UpdateOrderCommand r => r.Id,
            ChangeOrderStatusCommand r => r.Id,
            CancelOrderCommand r => r.Id,
            DeleteOrderCommand r => r.Id,
            AssignDeliveryManCommand r => r.Id,
            UnassignDeliveryManCommand r => r.Id,
            SetOrderPaidInStoreCashCommand r => r.OrderId,
            CreateBankPaymentCommand r => r.OrderId,
            CreateAppPaymentCommand r => r.OrderId,
            CreateReservationDepositCommand r => r.OrderId,
            _ => null
        };
        if (orderId.HasValue) return [orderId.Value];
        int? bankPaymentId = request switch
        {
            UpdateBankPaymentCommand r => r.Id,
            DeleteBankPaymentCommand r => r.Id,
            VerifyBankPaymentCommand r => r.Id,
            UnverifyBankPaymentCommand r => r.Id,
            _ => null
        };
        if (bankPaymentId.HasValue)
            return await db.BankPayments.AsNoTracking().Where(p => p.Id == bankPaymentId).Select(p => p.OrderId).ToListAsync(ct);
        int? appPaymentId = request switch
        {
            UpdateAppPaymentCommand r => r.Id,
            DeleteAppPaymentCommand r => r.Id,
            SettleAppPaymentCommand r => r.Id,
            UnsettleAppPaymentCommand r => r.Id,
            _ => null
        };
        if (appPaymentId.HasValue)
            return await db.AppPayments.AsNoTracking().Where(p => p.Id == appPaymentId).Select(p => p.OrderId).ToListAsync(ct);
        if (request is SettleMultipleAppPaymentsCommand multiple)
            return await db.AppPayments.AsNoTracking().Where(p => multiple.PaymentIds.Contains(p.Id)).Select(p => p.OrderId).ToListAsync(ct);
        int? depositId = request switch
        {
            UpdateReservationDepositCommand r => r.Id,
            DeleteReservationDepositCommand r => r.Id,
            _ => null
        };
        if (depositId.HasValue)
            return await db.ReservationDeposits.AsNoTracking().Where(p => p.Id == depositId).Select(p => p.OrderId).ToListAsync(ct);
        return null;
    }
}
