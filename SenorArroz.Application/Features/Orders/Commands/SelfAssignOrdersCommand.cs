using MediatR;
using SenorArroz.Application.Features.Orders.DTOs;

namespace SenorArroz.Application.Features.Orders.Commands;

public class SelfAssignOrdersCommand : IRequest<List<OrderDto>>
{
    public List<int> OrderIds { get; set; } = new();
    public string Password { get; set; } = string.Empty;
}
