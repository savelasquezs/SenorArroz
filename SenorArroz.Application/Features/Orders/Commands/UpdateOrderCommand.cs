using MediatR;
using SenorArroz.Application.Features.Orders.DTOs;

namespace SenorArroz.Application.Features.Orders.Commands;

public class UpdateOrderCommand : IRequest<OrderDto>
{
    public int Id { get; set; }
    public UpdateOrderDto Order { get; set; } = null!;
}
