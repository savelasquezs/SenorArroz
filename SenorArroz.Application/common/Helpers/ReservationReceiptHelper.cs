using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Application.Common.Helpers;

/// <summary>
/// A receipt is the financial source of truth; its linked operational payment only
/// keeps existing POS/mobile/settlement clients compatible. Call writes inside the
/// caller's order transaction. Never infer a link from equal amounts or dates.
/// </summary>
public static class ReservationReceiptHelper
{
    public static decimal CoveredAmount(Order order)
    {
        var bank = order.BankPayments.Where(p => !p.IsAppSettlement).Sum(p => p.Amount);
        var app = order.AppPayments.Where(p => !p.IsReversed).Sum(p => p.Amount);
        var cash = order.PaidInStoreCash
            ? order.PaidInStoreCashAmount ?? Math.Max(0m, order.Total - bank - app)
            : 0m;
        var unrepresented = order.Deposits.Where(d => d.IsEffective
            ? !d.CashAppliedToOrder
            : d.AppId.HasValue
                ? !order.AppPayments.Any(p => p.SourceReservationDepositId == d.Id)
                : !order.BankPayments.Any(p => p.SourceReservationDepositId == d.Id)).Sum(d => d.Amount);
        return bank + app + cash + unrepresented;
    }

    public static async Task<Order> LoadPaymentsAsync(IApplicationDbContext db, int orderId, CancellationToken ct)
        => await db.Orders.Include(o => o.BankPayments).Include(o => o.AppPayments)
            .Include(o => o.Deposits).AsSplitQuery().SingleAsync(o => o.Id == orderId, ct);

    public static async Task CreateInitialAsync(
        IApplicationDbContext db, int orderId, CreateOrderDto input, int actorId, DateTime receivedAt, CancellationToken ct)
    {
        await db.ReloadOrderForManagementAsync(orderId, ct);
        var order = await LoadPaymentsAsync(db, orderId, ct);
        var receipts = new List<ReservationDeposit>();
        foreach (var p in input.BankPayments ?? [])
            receipts.Add(new ReservationDeposit { BankId = p.BankId, Amount = p.Amount });
        foreach (var p in input.AppPayments ?? [])
            receipts.Add(new ReservationDeposit { AppId = p.AppId, Amount = p.Amount });
        var nonCash = receipts.Sum(d => d.Amount);
        if (input.PaidInStoreCash)
        {
            var cash = input.PaidInStoreCashAmount.HasValue
                ? input.PaidInStoreCashAmount.Value
                : Math.Max(0m, order.Total - nonCash);
            if (input.PaidInStoreCashAmount.HasValue || cash > 0)
                receipts.Add(new ReservationDeposit { IsEffective = true, Amount = cash });
        }
        foreach (var receipt in receipts)
        {
            receipt.OrderId = order.Id;
            receipt.BranchId = order.BranchId;
            receipt.TenantId = order.TenantId;
            receipt.ReceivedById = actorId;
            receipt.ReceivedAt = receivedAt;
        }
        await CreateAsync(db, order, receipts, ct);
    }

    public static async Task CreateAsync(
        IApplicationDbContext db, Order order, IReadOnlyCollection<ReservationDeposit> receipts, CancellationToken ct)
    {
        foreach (var d in receipts)
        {
            ValidateAmount(d, d.Amount);
            var methods = (d.IsEffective ? 1 : 0) + (d.BankId.HasValue ? 1 : 0) + (d.AppId.HasValue ? 1 : 0);
            if (methods != 1 || d.Id != 0 || d.OrderId != order.Id || d.BranchId != order.BranchId)
                throw new BusinessException("El abono debe tener un único medio de pago del pedido.");
        }
        if (CoveredAmount(order) + receipts.Sum(d => d.Amount) > order.Total)
            throw new BusinessException("Los pagos y abonos superan el valor del pedido.");
        if (receipts.Count == 0) return;

        // Mark cash before inserting; the matching order snapshot is written below
        // in the same transaction. This also makes update/delete unambiguous.
        foreach (var d in receipts) d.CashAppliedToOrder = d.IsEffective;
        db.ReservationDeposits.AddRange(receipts);
        await db.SaveChangesAsync(ct);
        foreach (var d in receipts)
        {
            if (d.BankId.HasValue)
                db.BankPayments.Add(new BankPayment
                {
                    TenantId = order.TenantId, OrderId = order.Id, BankId = d.BankId.Value,
                    Amount = d.Amount, SourceReservationDepositId = d.Id, IsVerified = false,
                });
            else if (d.AppId.HasValue)
                db.AppPayments.Add(new AppPayment
                {
                    TenantId = order.TenantId, OrderId = order.Id, AppId = d.AppId.Value,
                    Amount = d.Amount, SourceReservationDepositId = d.Id, IsSetted = false,
                });
        }
        await db.SaveChangesAsync(ct);
        var cash = receipts.Where(d => d.IsEffective).Sum(d => d.Amount);
        if (cash > 0) await AdjustCashAsync(db, order.Id, cash, receipts.Min(d => d.ReceivedAt), ct);
    }

    public static void ValidateAmount(ReservationDeposit deposit, decimal amount)
    {
        if (amount <= 0 || (deposit.IsEffective && (amount != decimal.Truncate(amount) || amount > int.MaxValue)))
            throw new BusinessException("El monto debe ser positivo; el efectivo debe expresarse en pesos enteros.");
    }

    public static async Task UpdateAsync(
        IApplicationDbContext db, ReservationDeposit deposit, decimal amount, CancellationToken ct)
    {
        ValidateAmount(deposit, amount);
        var order = await LoadPaymentsAsync(db, deposit.OrderId, ct);
        if (CoveredAmount(order) - deposit.Amount + amount > order.Total)
            throw new BusinessException("Los pagos y abonos superan el valor del pedido.");
        var app = order.AppPayments.SingleOrDefault(p => p.SourceReservationDepositId == deposit.Id);
        if (app is not null && (app.IsSetted || app.IsReversed))
            throw new BusinessException("No se puede modificar un abono cuya aplicación ya fue liquidada o reversada.");
        var bank = order.BankPayments.SingleOrDefault(p => p.SourceReservationDepositId == deposit.Id);
        var delta = amount - deposit.Amount;
        deposit.Amount = amount;
        if (bank is not null) bank.Amount = amount;
        if (app is not null) app.Amount = amount;
        await db.SaveChangesAsync(ct);
        if (deposit.CashAppliedToOrder)
            await AdjustCashAsync(db, order.Id, delta, deposit.ReceivedAt, ct);
    }

    public static async Task DeleteAsync(IApplicationDbContext db, ReservationDeposit deposit, CancellationToken ct)
    {
        var apps = await db.AppPayments.Where(p => p.SourceReservationDepositId == deposit.Id).ToListAsync(ct);
        if (apps.Any(p => p.IsSetted || p.IsReversed))
            throw new BusinessException("No se puede eliminar un abono cuya aplicación ya fue liquidada o reversada.");
        var banks = await db.BankPayments.Where(p => p.SourceReservationDepositId == deposit.Id).ToListAsync(ct);
        db.AppPayments.RemoveRange(apps);
        db.BankPayments.RemoveRange(banks);
        db.ReservationDeposits.Remove(deposit);
        await db.SaveChangesAsync(ct);
        if (deposit.CashAppliedToOrder)
            await AdjustCashAsync(db, deposit.OrderId, -deposit.Amount, deposit.ReceivedAt, ct);
    }

    private static async Task AdjustCashAsync(
        IApplicationDbContext db, int orderId, decimal delta, DateTime receivedAt, CancellationToken ct)
    {
        // Child-table triggers bump management_version. Refresh AFTER their save,
        // not before it, so the cash snapshot cannot produce a false 409.
        var order = await db.ReloadOrderForManagementAsync(orderId, ct)
            ?? throw new BusinessException("Pedido no encontrado.");
        var amount = checked((order.PaidInStoreCash ? order.PaidInStoreCashAmount ?? 0 : 0) + (int)delta);
        if (amount < 0) throw new BusinessException("El efectivo del pedido no coincide con sus abonos.");
        var hadCash = order.PaidInStoreCash;
        order.PaidInStoreCash = amount > 0;
        order.PaidInStoreCashAmount = amount > 0 ? amount : null;
        order.PaidInStoreCashAt = amount == 0 ? null : hadCash ? order.PaidInStoreCashAt : receivedAt;
        await db.SaveChangesAsync(ct);
    }
}
