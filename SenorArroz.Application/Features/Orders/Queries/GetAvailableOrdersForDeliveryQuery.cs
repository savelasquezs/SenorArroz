using MediatR;
using SenorArroz.Application.Features.Orders.DTOs;

namespace SenorArroz.Application.Features.Orders.Queries;

public class GetAvailableOrdersForDeliveryQuery : IRequest<List<OrderDto>>
{
    public int? BranchId { get; set; }
}
