using MediatR;
using SenorArroz.Application.Features.Orders.DTOs;

namespace SenorArroz.Application.Features.Orders.Queries;

public sealed class GetPreparationOrdersNearBranchQuery : IRequest<List<OrderDto>>
{
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
}
