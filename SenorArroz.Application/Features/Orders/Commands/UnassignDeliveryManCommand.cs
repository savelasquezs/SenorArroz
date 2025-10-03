using MediatR;
using SenorArroz.Application.Features.Orders.DTOs;

namespace SenorArroz.Application.Features.Orders.Commands;

public class UnassignDeliveryManCommand : IRequest<OrderDto>
{
    public int Id { get; set; }
}
