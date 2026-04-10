using MediatR;
using SenorArroz.Application.Features.DeliverymanAdvances.DTOs;

namespace SenorArroz.Application.Features.DeliverymanAdvances.Queries;

public class GetMyDeliverymanAdvancesQuery : IRequest<List<DeliverymanAdvanceDto>>
{
    public DateTime? FromDate { get; init; }
    public DateTime? ToDate { get; init; }
}
