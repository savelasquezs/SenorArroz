using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Common;

/// <summary>
/// Prepara el agregado <see cref="Order"/> antes de <c>DbSet.Update</c> cuando el pedido
/// se cargó con AsNoTracking e <c>Include</c>. Evita que EF marque entidades de catálogo o
/// lookup (Branch, User, Product, …) como <c>Modified</c> y falle <c>SaveChanges</c> con
/// <c>DbUpdateConcurrencyException</c> (0 filas). Mismo criterio que
/// <c>ExpenseRepository.UpdateAsync</c> (gastos).
/// </summary>
public static class OrderUpdateGraphForPersistence
{
    public static void DetachReadOnlyNavigations(Order order)
    {
        order.Branch = null!;
        order.TakenBy = null!;
        order.Customer = null!;
        order.Address = null!;
        order.LoyaltyCycleStep = null!;
        order.DeliveryMan = null!;
        order.DeliveryRoute = null!;

        if (order.OrderDetails is not { Count: > 0 })
            return;
        foreach (var d in order.OrderDetails)
            d.Product = null!;
    }
}
