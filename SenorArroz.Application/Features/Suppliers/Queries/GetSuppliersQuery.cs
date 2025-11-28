using MediatR;
using SenorArroz.Application.Features.Suppliers.DTOs;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.Suppliers.Queries;

public class GetSuppliersQuery : IRequest<PagedResult<SupplierDto>>
{
    public string? Search { get; set; }
    public int? BranchId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string? SortBy { get; set; } = "name";
    public string SortOrder { get; set; } = "asc";
}


