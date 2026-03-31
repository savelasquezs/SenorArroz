using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Common.Helpers;

/// <summary>
/// Parte en efectivo del pedido: <c>Total − bancos − apps</c> (misma regla que liquidación domiciliarios y caja).
/// </summary>
public static class OrderCashPortionHelper
{
    public static decimal GetCashPortion(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);
        var bank = order.BankPayments?.Sum(bp => bp.Amount) ?? 0m;
        var app = order.AppPayments?.Sum(ap => ap.Amount) ?? 0m;
        return order.Total - bank - app;
    }

    /// <summary>Monto a cobrar en entrega (no negativo; alineado con UI / ticket domiciliario).</summary>
    public static int GetCashToCollectDisplay(Order order)
    {
        var v = GetCashPortion(order);
        var rounded = (int)Math.Round(v, MidpointRounding.AwayFromZero);
        return Math.Max(0, rounded);
    }
}
