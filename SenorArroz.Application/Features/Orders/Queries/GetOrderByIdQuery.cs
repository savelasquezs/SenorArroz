using MediatR;
using SenorArroz.Application.Features.Orders.DTOs;

namespace SenorArroz.Application.Features.Orders.Queries;

public class GetOrderByIdQuery : IRequest<OrderDto?>
{
    public int Id { get; set; }
}
