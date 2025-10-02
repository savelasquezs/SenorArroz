// SenorArroz.Application/Features/Apps/Queries/GetAppsQuery.cs
using MediatR;
using SenorArroz.Application.Features.Apps.DTOs;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.Apps.Queries;

public class GetAppsQuery : IRequest<PagedResult<AppDto>>
{
    public int? BankId { get; set; }
    public string? Name { get; set; }
    public int? BranchId { get; set; }
    public bool? Active { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string SortBy { get; set; } = "name";
    public string SortOrder { get; set; } = "asc";
}
