namespace SenorArroz.Application.Features.Orders.DTOs;

public class SetOrderPaidInStoreCashDto
{
    public bool PaidInStoreCash { get; set; }

    /// <summary>
    /// Opcional. Si <see cref="PaidInStoreCash"/> es true, fija o ajusta el monto COP (validado contra el remanente).
    /// </summary>
    public int? PaidInStoreCashAmount { get; set; }
}
