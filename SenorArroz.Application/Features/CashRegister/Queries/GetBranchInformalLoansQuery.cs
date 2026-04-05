using MediatR;
using SenorArroz.Application.Features.CashRegister.DTOs;

namespace SenorArroz.Application.Features.CashRegister.Queries;

public class GetBranchInformalLoansQuery : IRequest<List<BranchInformalLoanDto>>
{
    public int? BranchId { get; set; }

    /// <summary>active (default), inactive, o all</summary>
    public string Scope { get; set; } = "active";
}
