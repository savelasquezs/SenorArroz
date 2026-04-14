using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Application.Common.Helpers;

public static class OrderPaidInStoreCashHelper
{
    /// <summary>
    /// Tope COP (entero) que puede registrarse como efectivo cobrado en tienda: total − bancos − apps.
    /// </summary>
    public static int ComputePaidInStoreCashCap(Order order)
    {
        var bank = order.BankPayments?.Sum(bp => bp.Amount) ?? 0m;
        var app = order.AppPayments?.Sum(ap => ap.Amount) ?? 0m;
        var raw = (decimal)order.Total - bank - app;
        return (int)Math.Round(Math.Max(0m, raw), MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Aplica o quita el marcador de efectivo cobrado en tienda (misma lógica que PUT paid-in-store-cash).
    /// </summary>
    /// <param name="explicitPaidInStoreCashAmount">
    /// Si viene con valor y <paramref name="paidInStoreCash"/> es true, fija ese monto (validado contra el tope).
    /// Si es null, al activar por primera vez se usa el snapshot del remanente.
    /// </param>
    public static void Apply(
        Order order,
        bool paidInStoreCash,
        DateTime utcNow,
        int? explicitPaidInStoreCashAmount = null)
    {
        if (!paidInStoreCash)
        {
            order.PaidInStoreCash = false;
            order.PaidInStoreCashAt = null;
            order.PaidInStoreCashAmount = null;
            return;
        }

        var cap = ComputePaidInStoreCashCap(order);
        var wasAlready = order.PaidInStoreCash;

        if (explicitPaidInStoreCashAmount.HasValue)
        {
            var v = explicitPaidInStoreCashAmount.Value;
            if (v < 1 || v > cap)
                throw new BusinessException(
                    cap < 1
                        ? "No hay remanente para registrar efectivo en tienda con el monto indicado."
                        : $"El monto en efectivo en tienda debe estar entre $1 y ${cap:N0}.");

            order.PaidInStoreCash = true;
            order.PaidInStoreCashAmount = v;
            if (!wasAlready)
                order.PaidInStoreCashAt = utcNow;
            return;
        }

        if (!wasAlready)
        {
            var snap = cap;
            order.PaidInStoreCashAmount = snap;
            order.PaidInStoreCashAt = utcNow;
        }

        order.PaidInStoreCash = true;
    }
}
