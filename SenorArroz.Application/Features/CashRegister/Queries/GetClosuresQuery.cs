using MediatR;
using SenorArroz.Application.Features.CashRegister.DTOs;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.CashRegister.Queries;

public class GetClosuresQuery : IRequest<PagedResult<CashClosureDto>>
{
    public int? BranchId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}
