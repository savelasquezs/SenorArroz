using SenorArroz.Application.Common.Helpers;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Tests;

public class ReservationReceiptCoverageTests
{
    [Fact]
    public void Partial_cash_does_not_mean_fully_paid()
    {
        var order = new Order { Total = 100000, PaidInStoreCash = true, PaidInStoreCashAmount = 30000 };
        Assert.Equal(70000m, OrderCashPortionHelper.GetCashPortion(order));
    }

    [Fact]
    public void Unrepresented_legacy_deposits_reduce_cash_without_migration()
    {
        var order = new Order { Total = 100000 };
        order.Deposits.Add(new ReservationDeposit { Id = 1, Amount = 20000, IsEffective = true });
        order.Deposits.Add(new ReservationDeposit { Id = 2, Amount = 10000, AppId = 1 });
        Assert.Equal(70000m, OrderCashPortionHelper.GetCashPortion(order));
    }

    [Fact]
    public void Linked_receipts_and_app_settlement_are_counted_exactly_once()
    {
        var order = new Order { Total = 100000, PaidInStoreCash = true, PaidInStoreCashAmount = 10000 };
        order.Deposits.Add(new ReservationDeposit { Id = 1, Amount = 20000, BankId = 1 });
        order.Deposits.Add(new ReservationDeposit { Id = 2, Amount = 30000, AppId = 1 });
        order.Deposits.Add(new ReservationDeposit { Id = 3, Amount = 10000, IsEffective = true, CashAppliedToOrder = true });
        order.BankPayments.Add(new BankPayment { Amount = 20000, SourceReservationDepositId = 1 });
        order.AppPayments.Add(new AppPayment { Amount = 30000, SourceReservationDepositId = 2, IsSetted = true });
        order.BankPayments.Add(new BankPayment { Amount = 30000, IsAppSettlement = true });
        Assert.Equal(40000m, OrderCashPortionHelper.GetCashPortion(order));
    }

    [Fact]
    public void Equal_unlinked_payments_are_not_deduplicated_by_amount()
    {
        var order = new Order { Total = 100000 };
        order.Deposits.Add(new ReservationDeposit { Id = 1, Amount = 20000, BankId = 1 });
        order.BankPayments.Add(new BankPayment { Amount = 20000, BankId = 1 });
        Assert.Equal(60000m, OrderCashPortionHelper.GetCashPortion(order));
    }

    [Fact]
    public void Cash_receipts_cannot_be_cleared_by_the_ordinary_cash_toggle()
    {
        var order = new Order { Total = 100000, PaidInStoreCash = true, PaidInStoreCashAmount = 10000 };
        order.Deposits.Add(new ReservationDeposit { Id = 1, Amount = 10000, IsEffective = true, CashAppliedToOrder = true });
        Assert.Throws<BusinessException>(() => OrderPaidInStoreCashHelper.Apply(order, false, DateTime.UtcNow));
        Assert.Throws<BusinessException>(() => OrderPaidInStoreCashHelper.Apply(order, true, DateTime.UtcNow, 5000));
        Assert.Equal(10000, order.PaidInStoreCashAmount);
    }
}
