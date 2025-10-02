// SenorArroz.Application/Features/Banks/Queries/GetBanksQuery.cs
using MediatR;
using SenorArroz.Application.Features.Banks.DTOs;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.Banks.Queries;

public class GetBanksQuery : IRequest<PagedResult<BankDto>>
{
    public int? BranchId { get; set; }
    public string? Name { get; set; }
    public bool? Active { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string SortBy { get; set; } = "name";
    public string SortOrder { get; set; } = "asc";
}
