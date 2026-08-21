using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Application.Features.AppPayments.Commands;

public sealed class SettleMultipleAppPaymentsHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IBranchContext branchContext)
    : IRequestHandler<SettleMultipleAppPaymentsCommand, bool>
{
    public async Task<bool> Handle(
        SettleMultipleAppPaymentsCommand request,
        CancellationToken ct)
    {
        if (!Roles.IsSuperadminOrAdminOrCashier(currentUser.Role))
            throw new BusinessException("Solo administradores y cajeros pueden liquidar pagos de apps.");

        var ids = request.PaymentIds.Distinct().ToList();
        if (ids.Count == 0)
            throw new BusinessException("Se requiere al menos un pago.");

        var payments = await db.AppPayments
            .Include(x => x.App)
                .ThenInclude(x => x.Bank)
            .Include(x => x.Order)
            .Where(x => ids.Contains(x.Id))
            .OrderBy(x => x.Id)
            .ToListAsync(ct);
        if (payments.Count != ids.Count)
            throw new BusinessException("Uno o más pagos seleccionados no existen.");
        if (payments.Any(x => x.IsSetted))
            throw new BusinessException("Uno o más pagos ya fueron liquidados.");
        if (payments.Any(x => x.IsReversed))
            throw new BusinessException("No se puede liquidar un pago revertido.");

        var branchIds = payments.Select(x => x.Order.BranchId).Distinct().ToList();
        var appIds = payments.Select(x => x.AppId).Distinct().ToList();
        var bankIds = payments.Select(x => x.App.BankId).Distinct().ToList();
        if (branchIds.Count != 1 || appIds.Count != 1 || bankIds.Count != 1)
            throw new BusinessException(
                "La liquidación debe contener pagos de una sola sucursal, app y banco.");
        branchContext.EnsureAccess(branchIds[0]);
        if (!Roles.IsSuperadmin(currentUser.Role) && currentUser.BranchId != branchIds[0])
            throw new BusinessException("No tienes permisos para liquidar estos pagos.");

        var containsEstimatedNet = payments.Any(x => x.ExpectedNetAmount.HasValue);
        if (containsEstimatedNet && !request.ActualAmount.HasValue)
            throw new BusinessException(
                "Ingresa el valor real consignado por la app para liquidar pedidos Rappi.");
        var expectedTotal = payments.Sum(x => x.ExpectedNetAmount ?? x.Amount);
        var actualTotal = request.ActualAmount ?? expectedTotal;
        if (actualTotal <= 0 || expectedTotal <= 0)
            throw new BusinessException("Los valores de la liquidación deben ser mayores que cero.");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var allocated = 0m;
        for (var index = 0; index < payments.Count; index++)
        {
            var payment = payments[index];
            var expected = payment.ExpectedNetAmount ?? payment.Amount;
            var actual = index == payments.Count - 1
                ? actualTotal - allocated
                : decimal.Round(
                    actualTotal * expected / expectedTotal,
                    2,
                    MidpointRounding.AwayFromZero);
            allocated += actual;
            payment.ActualSettledAmount = actual;
            payment.SettlementVariance = actual - expected;
            payment.IsSetted = true;
        }

        db.BankPayments.Add(new BankPayment
        {
            OrderId = payments[0].OrderId,
            BankId = bankIds[0],
            Amount = actualTotal,
            IsAppSettlement = true,
            AppSettlementSourcePaymentIds =
                AppSettlementBankPaymentSourceIds.Serialize(payments.Select(x => x.Id))
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }
}
