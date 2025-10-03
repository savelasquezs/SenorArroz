using MediatR;
using SenorArroz.Application.Features.Orders.DTOs;

namespace SenorArroz.Application.Features.Orders.Queries;

public class GetOrderWithDetailsQuery : IRequest<OrderWithDetailsDto?>
{
    public int Id { get; set; }
}
