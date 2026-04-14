using MediatR;
using SenorArroz.Application.Features.Orders.DTOs;

namespace SenorArroz.Application.Features.Orders.Commands;

public class SetOrderPaidInStoreCashCommand : IRequest<OrderDto>
{
    public int OrderId { get; set; }
    public bool PaidInStoreCash { get; set; }
    public int? PaidInStoreCashAmount { get; set; }
}
