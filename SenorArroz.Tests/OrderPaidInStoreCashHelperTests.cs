using SenorArroz.Application.Common.Helpers;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Tests;

public class OrderPaidInStoreCashHelperTests
{
    [Fact]
    public void Apply_false_clears_paid_in_store_fields()
    {
        var utc = new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);
        var order = new Order
        {
            Total = 5000,
            PaidInStoreCash = true,
            PaidInStoreCashAmount = 5000,
            PaidInStoreCashAt = utc,
        };

        OrderPaidInStoreCashHelper.Apply(order, false, utc, null);

        Assert.False(order.PaidInStoreCash);
        Assert.Null(order.PaidInStoreCashAt);
        Assert.Null(order.PaidInStoreCashAmount);
    }

    [Fact]
    public void Apply_true_first_time_without_explicit_sets_snapshot()
    {
        var utc = new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);
        var order = new Order
        {
            Total = 12_000,
            PaidInStoreCash = false,
            BankPayments = new List<BankPayment>(),
            AppPayments = new List<AppPayment>(),
        };

        OrderPaidInStoreCashHelper.Apply(order, true, utc, null);

        Assert.True(order.PaidInStoreCash);
        Assert.Equal(12_000, order.PaidInStoreCashAmount);
        Assert.Equal(utc, order.PaidInStoreCashAt);
    }

    [Fact]
    public void Apply_true_with_explicit_amount_respects_bank_payments()
    {
        var utc = new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);
        var order = new Order
        {
            Total = 10_000,
            PaidInStoreCash = false,
            BankPayments = new List<BankPayment> { new() { Amount = 4000m } },
            AppPayments = new List<AppPayment>(),
        };

        OrderPaidInStoreCashHelper.Apply(order, true, utc, 3000);

        Assert.True(order.PaidInStoreCash);
        Assert.Equal(3000, order.PaidInStoreCashAmount);
        Assert.Equal(utc, order.PaidInStoreCashAt);
    }

    [Fact]
    public void Apply_true_when_already_set_without_explicit_keeps_amount()
    {
        var utc0 = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc);
        var utc1 = new DateTime(2026, 4, 1, 11, 0, 0, DateTimeKind.Utc);
        var order = new Order
        {
            Total = 8000,
            PaidInStoreCash = true,
            PaidInStoreCashAmount = 8000,
            PaidInStoreCashAt = utc0,
            BankPayments = new List<BankPayment>(),
            AppPayments = new List<AppPayment>(),
        };

        OrderPaidInStoreCashHelper.Apply(order, true, utc1, null);

        Assert.True(order.PaidInStoreCash);
        Assert.Equal(8000, order.PaidInStoreCashAmount);
        Assert.Equal(utc0, order.PaidInStoreCashAt);
    }

    [Fact]
    public void Apply_explicit_throws_when_amount_above_cap()
    {
        var order = new Order
        {
            Total = 100,
            PaidInStoreCash = false,
            BankPayments = new List<BankPayment>(),
            AppPayments = new List<AppPayment>(),
        };

        var ex = Assert.Throws<BusinessException>(() =>
            OrderPaidInStoreCashHelper.Apply(order, true, DateTime.UtcNow, 200));
        Assert.NotEmpty(ex.Message);
    }

    [Fact]
    public void Apply_explicit_throws_when_amount_below_one()
    {
        var order = new Order
        {
            Total = 5000,
            PaidInStoreCash = false,
            BankPayments = new List<BankPayment>(),
            AppPayments = new List<AppPayment>(),
        };

        Assert.Throws<BusinessException>(() =>
            OrderPaidInStoreCashHelper.Apply(order, true, DateTime.UtcNow, 0));
    }

    [Fact]
    public void ComputePaidInStoreCashCap_matches_total_minus_payments()
    {
        var order = new Order
        {
            Total = 9000,
            BankPayments = new List<BankPayment> { new() { Amount = 1000m }, new() { Amount = 500m } },
            AppPayments = new List<AppPayment> { new() { Amount = 2500m } },
        };

        Assert.Equal(5000, OrderPaidInStoreCashHelper.ComputePaidInStoreCashCap(order));
    }
}
