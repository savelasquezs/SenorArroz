using MediatR;
using SenorArroz.Application.Features.Orders.DTOs;

namespace SenorArroz.Application.Features.Orders.Commands;

public sealed class TransferOrderBranchCommand : IRequest<OrderDto>
{
    public int Id { get; set; }
    public int TargetBranchId { get; set; }
}
