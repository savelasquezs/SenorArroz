// SenorArroz.Application/Features/Products/Queries/GetProductCategoriesHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.Products.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.Products.Queries;

public class GetProductCategoriesHandler : IRequestHandler<GetProductCategoriesQuery, PagedResult<ProductCategoryDto>>
{
    private readonly IProductCategoryRepository _categoryRepository;
    private readonly IMapper _mapper;

    public GetProductCategoriesHandler(
        IProductCategoryRepository categoryRepository,
        IMapper mapper)
    {
        _categoryRepository = categoryRepository;
        _mapper = mapper;
    }

    public async Task<PagedResult<ProductCategoryDto>> Handle(GetProductCategoriesQuery request, CancellationToken cancellationToken)
    {
        // Catálogo compartido: todas las sucursales ven todas las categorías.
        int? branchFilter = request.BranchId.HasValue && request.BranchId.Value > 0
            ? request.BranchId.Value
            : null;

        var pagedCategories = await _categoryRepository.GetPagedAsync(
            branchFilter,
            request.Name,
            request.Page,
            request.PageSize,
            request.SortBy,
            request.SortOrder);

        var categoryDtos = new List<ProductCategoryDto>();

        foreach (var category in pagedCategories.Items)
        {
            var categoryDto = _mapper.Map<ProductCategoryDto>(category);

            // Add statistics
            categoryDto.TotalProducts = await _categoryRepository.GetTotalProductsAsync(category.Id);
            categoryDto.ActiveProducts = await _categoryRepository.GetActiveProductsAsync(category.Id);

            categoryDtos.Add(categoryDto);
        }

        return new PagedResult<ProductCategoryDto>
        {
            Items = categoryDtos,
            TotalCount = pagedCategories.TotalCount,
            Page = pagedCategories.Page,
            PageSize = pagedCategories.PageSize,
            TotalPages = pagedCategories.TotalPages
        };
    }
}