using MediatR;
using SenorArroz.Application.Features.CashRegister.DTOs;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.CashRegister.Queries;

public class GetCashVaultMovementsQuery : IRequest<PagedResult<CashVaultMovementDto>>
{
    public int? BranchId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}
