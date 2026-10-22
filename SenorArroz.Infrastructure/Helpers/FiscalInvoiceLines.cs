using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Infrastructure.Helpers;

public static class FiscalInvoiceLines
{
    public sealed record Line(OrderDetail Source, decimal Net, bool Gift);

    public static IReadOnlyList<Line> Build(Order order)
    {
        var lines = order.OrderDetails.ToArray();
        if (lines.Any(x => x.Quantity <= 0 || x.UnitPrice < 0 || x.Discount < 0 || x.Discount > (decimal)x.UnitPrice * x.Quantity))
            throw new BusinessException("Línea de pedido inválida para facturación.");
        var discounts = lines.Where(x => x.Discount < (decimal)x.UnitPrice * x.Quantity).Sum(x => (decimal)x.Discount);
        var amounts = lines.ToDictionary(x => x, x => x.Discount == (decimal)x.UnitPrice * x.Quantity ? 0m : (decimal)x.UnitPrice * x.Quantity);
        foreach (var line in lines.Where(x => amounts[x] > 0).OrderByDescending(x => amounts[x]).ThenBy(x => x.ProductId).ThenBy(x => x.Id))
        {
            var amount = Math.Min(discounts, amounts[line]);
            amounts[line] -= amount;
            discounts -= amount;
        }
        if (discounts != 0) throw new BusinessException("El descuento supera los productos facturables.");
        return lines.Select(x => new Line(x, amounts[x], x.Discount == (decimal)x.UnitPrice * x.Quantity)).ToArray();
    }
}
