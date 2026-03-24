using MediatR;

namespace SenorArroz.Application.Features.Deliverymen.Commands;

public class UnlockDeliverymanDayCommand : IRequest<Unit>
{
    public int DeliverymanId { get; set; }

    /// <summary>Fecha operativa YYYY-MM-DD (Colombia).</summary>
    public string Date { get; set; } = string.Empty;
}
