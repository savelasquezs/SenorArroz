using MediatR;
using SenorArroz.Application.Features.Orders.DTOs;

namespace SenorArroz.Application.Features.Orders.Commands;

public class ChangeOrderStatusCommand : IRequest<OrderDto>
{
    public int Id { get; set; }
    public ChangeOrderStatusDto StatusChange { get; set; } = null!;
}
