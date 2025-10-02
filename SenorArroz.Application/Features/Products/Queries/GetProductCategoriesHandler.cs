// SenorArroz.Application/Features/Products/Queries/GetProductCategoriesHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Products.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.Products.Queries;

public class GetProductCategoriesHandler : IRequestHandler<GetProductCategoriesQuery, PagedResult<ProductCategoryDto>>
{
    private readonly IProductCategoryRepository _categoryRepository;
    private readonly ICurrentUser _currentUser;
    private readonly IMapper _mapper;

    public GetProductCategoriesHandler(
        IProductCategoryRepository categoryRepository,
        ICurrentUser currentUser,
        IMapper mapper)
    {
        _categoryRepository = categoryRepository;
        _currentUser = currentUser;
        _mapper = mapper;
    }

    public async Task<PagedResult<ProductCategoryDto>> Handle(GetProductCategoriesQuery request, CancellationToken cancellationToken)
    {
        // Determine branch filter based on user role
        int? branchFilter = null;

        if (_currentUser.Role != "superadmin")
        {
            // Admin and other roles only see their branch
            branchFilter = _currentUser.BranchId;
        }
        else if (request.BranchId.HasValue && request.BranchId > 0)
        {
            // Superadmin can optionally filter by specific branch
            branchFilter = request.BranchId;
        }
        // If branchFilter is null, superadmin gets all categories from all branches

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