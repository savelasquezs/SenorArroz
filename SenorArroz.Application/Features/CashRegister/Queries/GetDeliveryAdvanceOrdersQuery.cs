using MediatR;
using SenorArroz.Application.Features.CashRegister.DTOs;

namespace SenorArroz.Application.Features.CashRegister.Queries;

public class GetDeliveryAdvanceOrdersQuery : IRequest<List<DeliveryAdvanceOrderRowDto>>
{
    public int? BranchId { get; set; }
}
