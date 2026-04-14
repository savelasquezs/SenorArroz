using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Kitchen;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using Xunit;

namespace SenorArroz.Tests;

public class KitchenNotificationEligibilityAndDiffTests
{
    private static Order BaseOrder() => new()
    {
        Id = 1,
        BranchId = 1,
        TakenById = 1,
        Status = OrderStatus.Taken,
        Type = OrderType.Delivery,
        StatusTimes = "{}",
    };

    [Fact]
    public void Eligibility_onsite_taken_is_visible()
    {
        var o = BaseOrder();
        o.Type = OrderType.Onsite;
        o.Status = OrderStatus.Taken;
        Assert.True(KitchenOrderNotificationEligibility.IsVisibleToActiveKitchen(o, DateTime.UtcNow));
    }

    [Fact]
    public void Eligibility_ready_is_not_visible()
    {
        var o = BaseOrder();
        o.Status = OrderStatus.Ready;
        Assert.False(KitchenOrderNotificationEligibility.IsVisibleToActiveKitchen(o, DateTime.UtcNow));
    }

    [Fact]
    public void Eligibility_reservation_future_kitchen_entry_not_visible()
    {
        var now = new DateTime(2026, 6, 15, 14, 0, 0, DateTimeKind.Utc);
        var o = BaseOrder();
        o.Type = OrderType.Reservation;
        o.Status = OrderStatus.Taken;
        o.PrepareAt = now.AddHours(2);
        Assert.False(KitchenOrderNotificationEligibility.IsVisibleToActiveKitchen(o, now));
    }

    [Fact]
    public void Eligibility_reservation_past_kitchen_entry_visible()
    {
        var now = new DateTime(2026, 6, 15, 14, 0, 0, DateTimeKind.Utc);
        var o = BaseOrder();
        o.Type = OrderType.Reservation;
        o.Status = OrderStatus.InPreparation;
        o.PrepareAt = now.AddHours(-1);
        Assert.True(KitchenOrderNotificationEligibility.IsVisibleToActiveKitchen(o, now));
    }

    [Fact]
    public void Diff_one_remove_one_add_becomes_single_replacement()
    {
        var before = new List<DetailSnap>
        {
            new(1, 10, 2, "Arroz"),
        };
        var after = new List<DetailSnap>
        {
            new(2, 20, 1, "Pollo"),
        };
        var s = KitchenOrderModificationDiff.Build(before, after);
        Assert.Empty(s.RemovedLines);
        Assert.Empty(s.AddedLines);
        Assert.Single(s.ProductReplacements);
        Assert.Equal("Arroz", s.ProductReplacements[0].PreviousProductName);
        Assert.Equal("Pollo", s.ProductReplacements[0].NewProductName);
    }

    [Fact]
    public void Diff_quantity_change()
    {
        var before = new List<DetailSnap> { new(1, 10, 2, "Arroz") };
        var after = new List<DetailSnap> { new(1, 10, 5, "Arroz") };
        var s = KitchenOrderModificationDiff.Build(before, after);
        Assert.Single(s.QuantityChanges);
        Assert.Equal(2, s.QuantityChanges[0].PreviousQuantity);
        Assert.Equal(5, s.QuantityChanges[0].NewQuantity);
    }

    [Fact]
    public void Diff_same_line_product_swap()
    {
        var before = new List<DetailSnap> { new(1, 10, 1, "Arroz") };
        var after = new List<DetailSnap> { new(1, 20, 1, "Pollo") };
        var s = KitchenOrderModificationDiff.Build(before, after);
        Assert.Single(s.ProductReplacements);
        Assert.Equal("Arroz", s.ProductReplacements[0].PreviousProductName);
        Assert.Equal("Pollo", s.ProductReplacements[0].NewProductName);
    }

    [Fact]
    public void Diff_add_only()
    {
        var before = new List<DetailSnap> { new(1, 10, 1, "A") };
        var after = new List<DetailSnap>
        {
            new(1, 10, 1, "A"),
            new(2, 20, 3, "B"),
        };
        var s = KitchenOrderModificationDiff.Build(before, after);
        Assert.Single(s.AddedLines);
        Assert.Equal("B", s.AddedLines[0].ProductName);
        Assert.Equal(3, s.AddedLines[0].Quantity);
    }

    [Fact]
    public void Diff_remove_only()
    {
        var before = new List<DetailSnap>
        {
            new(1, 10, 1, "A"),
            new(2, 20, 1, "B"),
        };
        var after = new List<DetailSnap> { new(1, 10, 1, "A") };
        var s = KitchenOrderModificationDiff.Build(before, after);
        Assert.Single(s.RemovedLines);
        Assert.Equal("B", s.RemovedLines[0].ProductName);
    }
}
