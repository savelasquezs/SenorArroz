using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Common.Helpers;

public static class OrderPaidInStoreCashHelper
{
    /// <summary>
    /// Aplica o quita el marcador de efectivo cobrado en tienda (misma lógica que PUT paid-in-store-cash).
    /// </summary>
    public static void Apply(Order order, bool paidInStoreCash, DateTime utcNow)
    {
        if (paidInStoreCash)
        {
            if (!order.PaidInStoreCash)
            {
                var bank = order.BankPayments?.Sum(bp => bp.Amount) ?? 0m;
                var app = order.AppPayments?.Sum(ap => ap.Amount) ?? 0m;
                var raw = (decimal)order.Total - bank - app;
                var snap = (int)Math.Round(Math.Max(0m, raw), MidpointRounding.AwayFromZero);
                order.PaidInStoreCashAmount = snap;
                order.PaidInStoreCashAt = utcNow;
            }

            order.PaidInStoreCash = true;
        }
        else
        {
            order.PaidInStoreCash = false;
            order.PaidInStoreCashAt = null;
            order.PaidInStoreCashAmount = null;
        }
    }
}
