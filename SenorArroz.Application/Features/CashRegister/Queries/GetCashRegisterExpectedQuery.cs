using MediatR;
using SenorArroz.Application.Features.CashRegister.DTOs;

namespace SenorArroz.Application.Features.CashRegister.Queries;

public class GetCashRegisterExpectedQuery : IRequest<CashRegisterExpectedDto>
{
    public int? BranchId { get; set; }
}
