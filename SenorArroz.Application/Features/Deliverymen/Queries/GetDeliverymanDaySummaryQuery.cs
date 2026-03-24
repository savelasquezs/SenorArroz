using MediatR;
using SenorArroz.Application.Features.Deliverymen.DTOs;

namespace SenorArroz.Application.Features.Deliverymen.Queries;

public class GetDeliverymanDaySummaryQuery : IRequest<DeliverymanDaySummaryDto>
{
    public int DeliverymanId { get; set; }
    public DateTime? Date { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }

    /// <summary>Base inicial usada en UI; si null, se usa la base por defecto del sistema.</summary>
    public decimal? BaseAmount { get; set; }
}
