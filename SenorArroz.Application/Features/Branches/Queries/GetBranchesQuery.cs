using MediatR;
using SenorArroz.Application.Features.Branches.DTOs;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.Branches.Queries;

public class GetBranchesQuery : IRequest<PagedResult<BranchDto>>
{
    public string? Name { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string SortBy { get; set; } = "name";
    public string SortOrder { get; set; } = "asc";
}