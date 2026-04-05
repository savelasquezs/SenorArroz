using MediatR;
using SenorArroz.Application.Features.Deliverymen.DTOs;

namespace SenorArroz.Application.Features.Deliverymen.Queries;

public class GetMyDeliverymanDayStateQuery : IRequest<MyDeliverymanDayStateDto>
{
    /// <summary>Fecha calendario YYYY-MM-DD (Colombia). Si null, se usa hoy en Colombia.</summary>
    public string? Date { get; set; }
}
