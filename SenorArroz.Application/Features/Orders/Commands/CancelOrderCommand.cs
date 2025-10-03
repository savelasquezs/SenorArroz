using MediatR;
using SenorArroz.Application.Features.Orders.DTOs;

namespace SenorArroz.Application.Features.Orders.Commands;

public class CancelOrderCommand : IRequest<OrderDto>
{
    public int Id { get; set; }
    public CancelOrderDto Cancellation { get; set; } = null!;
}
