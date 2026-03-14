using MediatR;
using SenorArroz.Application.Features.CashRegister.DTOs;

namespace SenorArroz.Application.Features.CashRegister.Queries;

public class GetLastClosureQuery : IRequest<CashClosureDto?>
{
    public int? BranchId { get; set; }
}
