using MediatR;
using SenorArroz.Application.Features.Orders.DTOs;

namespace SenorArroz.Application.Features.Orders.Commands;

public class AssignDeliveryManCommand : IRequest<OrderDto>
{
    public int Id { get; set; }
    public AssignDeliveryManDto Assignment { get; set; } = null!;
}
