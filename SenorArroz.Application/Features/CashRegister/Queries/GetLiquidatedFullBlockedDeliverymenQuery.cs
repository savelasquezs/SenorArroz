using MediatR;
using SenorArroz.Application.Features.CashRegister.DTOs;

namespace SenorArroz.Application.Features.CashRegister.Queries;

public class GetLiquidatedFullBlockedDeliverymenQuery : IRequest<List<LiquidatedDeliverymanOptionDto>>
{
    public int? BranchId { get; set; }
}
