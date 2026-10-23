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

    [Theory]
    [InlineData(null, "Sin cebolla")]
    [InlineData("Sin cebolla", "Poco picante")]
    [InlineData("Sin cebolla", "")]
    public void Diff_detects_note_add_edit_and_removal_on_the_correct_line(string? previous, string notes)
    {
        var before = new[] { new DetailSnap(1, 10, 1, "Arroz", previous), new DetailSnap(2, 10, 2, "Arroz", "Separar") };
        var after = new[] { new DetailSnap(1, 10, 1, "Arroz", notes), before[1] };

        var summary = KitchenOrderModificationDiff.Build(before, after);

        var change = Assert.Single(summary.LineNoteChanges);
        Assert.Equal("Arroz", change.ProductName);
        Assert.Equal(1, change.Quantity);
        Assert.Equal(previous, change.PreviousNotes);
        Assert.Equal(string.IsNullOrWhiteSpace(notes) ? null : notes, change.Notes);
        Assert.Empty(summary.QuantityChanges);
        Assert.Empty(summary.AddedLines);
        Assert.Empty(summary.ProductReplacements);
    }

    [Fact]
    public void Diff_preserves_both_quantity_and_note_changes()
    {
        var summary = KitchenOrderModificationDiff.Build(
            [new(1, 10, 1, "Arroz", "Sin cebolla")],
            [new(1, 10, 3, "Arroz", "Separar")]);

        Assert.Single(summary.QuantityChanges);
        Assert.Equal(3, Assert.Single(summary.LineNoteChanges).Quantity);
    }

    [Theory]
    [InlineData(null, " ")]
    [InlineData(" Sin cebolla ", "Sin cebolla")]
    public void Diff_ignores_equivalent_notes(string? before, string after)
    {
        var summary = KitchenOrderModificationDiff.Build(
            [new(1, 10, 1, "Arroz", before)], [new(1, 10, 1, "Arroz", after)]);

        Assert.Empty(summary.LineNoteChanges);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Diff_replacement_keeps_the_new_product_note_and_quantity(int newLineId)
    {
        var summary = KitchenOrderModificationDiff.Build(
            [new(1, 10, 1, "Arroz", "Nota anterior")],
            [new(newLineId, 20, 2, "Pollo", "Sin salsa")]);

        var replacement = Assert.Single(summary.ProductReplacements);
        Assert.Equal("Pollo", replacement.NewProductName);
        Assert.Equal(2, replacement.NewQuantity);
        Assert.Equal("Sin salsa", replacement.NewNotes);
        Assert.Empty(summary.LineNoteChanges);
        Assert.Empty(summary.QuantityChanges);
    }

    [Fact]
    public void Snapshot_and_diff_preserve_notes_when_an_order_entity_is_mutated()
    {
        var order = BaseOrder();
        order.OrderDetails.Add(new OrderDetail
        {
            Id = 1, ProductId = 10, Quantity = 2, Notes = "Sin cebolla",
            Product = new Product { Name = "Arroz paisa" },
        });
        var before = KitchenOrderModificationDiff.SnapshotFromOrder(order);
        order.OrderDetails.Single().Notes = "Separar";

        var summary = KitchenOrderModificationDiff.Build(before, KitchenOrderModificationDiff.SnapshotFromOrder(order));

        Assert.Equal("Sin cebolla", Assert.Single(summary.LineNoteChanges).PreviousNotes);
        Assert.Equal("Separar", summary.LineNoteChanges[0].Notes);
    }

    [Fact]
    public void Diff_added_line_keeps_its_note()
    {
        var summary = KitchenOrderModificationDiff.Build([], [new(1, 10, 2, "Arroz", "Sin cebolla")]);
        Assert.Equal("Sin cebolla", Assert.Single(summary.AddedLines).Notes);
    }
}
