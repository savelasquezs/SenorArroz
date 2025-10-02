// SenorArroz.Application/Features/Products/Queries/GetProductsQuery.cs
using MediatR;
using SenorArroz.Application.Features.Products.DTOs;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.Products.Queries;

public class GetProductsQuery : IRequest<PagedResult<ProductDto>>
{
    public int? BranchId { get; set; }
    public string? Name { get; set; }
    public int? CategoryId { get; set; }
    public bool? Active { get; set; }
    public int? MinPrice { get; set; }
    public int? MaxPrice { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string SortBy { get; set; } = "name";
    public string SortOrder { get; set; } = "asc";
}
