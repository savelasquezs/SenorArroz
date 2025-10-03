using MediatR;
using SenorArroz.Application.Features.Orders.DTOs;

namespace SenorArroz.Application.Features.Orders.Commands;

public class CreateOrderCommand : IRequest<OrderDto>
{
    public CreateOrderDto Order { get; set; } = null!;
}
