using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Common.Kitchen;

public readonly record struct DetailSnap(int Id, int ProductId, int Quantity, string ProductName, string? Notes = null);

/// <summary>
/// Compara dos snapshots de líneas del pedido y arma <see cref="KitchenOrderModificationSummary"/> para cocina.
/// </summary>
public static class KitchenOrderModificationDiff
{
    public static KitchenOrderModificationSummary Build(IReadOnlyList<DetailSnap> before, IReadOnlyList<DetailSnap> after)
    {
        var beforeById = before.Where(b => b.Id > 0).ToDictionary(b => b.Id);
        var afterById = after.Where(a => a.Id > 0).ToDictionary(a => a.Id);

        var summary = new KitchenOrderModificationSummary();

        foreach (var (id, b) in beforeById)
        {
            if (!afterById.TryGetValue(id, out var a))
            {
                summary.RemovedLines.Add(new KitchenOrderRemovedLineDto { ProductName = b.ProductName });
                continue;
            }

            if (b.ProductId != a.ProductId)
            {
                summary.ProductReplacements.Add(new KitchenOrderProductReplacementDto
                {
                    PreviousProductName = b.ProductName,
                    NewProductName = a.ProductName,
                    NewQuantity = a.Quantity,
                    NewNotes = NormalizeNotes(a.Notes),
                });
            }
            else if (b.Quantity != a.Quantity)
            {
                summary.QuantityChanges.Add(new KitchenOrderQuantityChangeDto
                {
                    ProductName = a.ProductName,
                    PreviousQuantity = b.Quantity,
                    NewQuantity = a.Quantity,
                });
            }

            if (b.ProductId == a.ProductId
                && !string.Equals(NormalizeNotes(b.Notes), NormalizeNotes(a.Notes), StringComparison.Ordinal))
            {
                summary.LineNoteChanges.Add(new KitchenOrderLineNoteChangeDto
                {
                    ProductName = a.ProductName,
                    Quantity = a.Quantity,
                    PreviousNotes = NormalizeNotes(b.Notes),
                    Notes = NormalizeNotes(a.Notes),
                });
            }
        }

        foreach (var (id, a) in afterById)
        {
            if (!beforeById.ContainsKey(id))
                summary.AddedLines.Add(new KitchenOrderAddedLineDto
                {
                    ProductName = a.ProductName, Quantity = a.Quantity, Notes = NormalizeNotes(a.Notes),
                });
        }

        if (summary.RemovedLines.Count == 1
            && summary.AddedLines.Count == 1
            && summary.QuantityChanges.Count == 0
            && summary.ProductReplacements.Count == 0)
        {
            summary.ProductReplacements.Add(new KitchenOrderProductReplacementDto
            {
                PreviousProductName = summary.RemovedLines[0].ProductName,
                NewProductName = summary.AddedLines[0].ProductName,
                NewQuantity = summary.AddedLines[0].Quantity,
                NewNotes = summary.AddedLines[0].Notes,
            });
            summary.RemovedLines.Clear();
            summary.AddedLines.Clear();
        }

        return summary;
    }

    public static IReadOnlyList<DetailSnap> SnapshotFromOrder(Order order) =>
        order.OrderDetails.Select(d => new DetailSnap(
            d.Id,
            d.ProductId,
            d.Quantity,
            string.IsNullOrWhiteSpace(d.Product?.Name) ? $"Producto #{d.ProductId}" : d.Product!.Name.Trim(),
            NormalizeNotes(d.Notes))).ToList();

    private static string? NormalizeNotes(string? notes) => string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
}
