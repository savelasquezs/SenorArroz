// SenorArroz.Application/Features/Products/Queries/GetProductCategoriesQuery.cs
using MediatR;
using SenorArroz.Application.Features.Products.DTOs;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.Products.Queries;

public class GetProductCategoriesQuery : IRequest<PagedResult<ProductCategoryDto>>
{
    public string? Name { get; set; }
    public int? BranchId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string SortBy { get; set; } = "name";
    public string SortOrder { get; set; } = "asc";
}