using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Common.Helpers;

/// <summary>
/// Alinea subtotal, descuentos y total del pedido con las líneas y el domicilio (misma fórmula que actualización).
/// </summary>
public static class OrderTotalsHelper
{
    public static void RecalculateFromOrderDetails(Order order)
    {
        order.Subtotal = order.OrderDetails.Sum(d => d.Quantity * d.UnitPrice);
        order.DiscountTotal = order.OrderDetails.Sum(d => d.Discount);
        order.Total = order.OrderDetails.Sum(d => (d.Subtotal ?? (d.Quantity * d.UnitPrice - d.Discount)))
            + (order.DeliveryFee ?? 0);
    }
}
