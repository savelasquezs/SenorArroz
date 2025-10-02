// SenorArroz.Application/Features/Products/Queries/GetProductsHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Products.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.Products.Queries;

public class GetProductsHandler : IRequestHandler<GetProductsQuery, PagedResult<ProductDto>>
{
    private readonly IProductRepository _productRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public GetProductsHandler(IProductRepository productRepository, IMapper mapper, ICurrentUser currentUser)
    {
        _productRepository = productRepository;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<PagedResult<ProductDto>> Handle(GetProductsQuery request, CancellationToken cancellationToken)
    {
        // Determine branch filter based on user role
        int? branchFilter = null;
        if (_currentUser.Role != "superadmin")
        {
            branchFilter = _currentUser.BranchId;
        }
        else if (request.BranchId > 0)
        {
            // Superadmin can optionally filter by specific branch
            branchFilter = request.BranchId;
        }
        // If branchFilter is null, superadmin gets all products from all branches

        var pagedProducts = await _productRepository.GetPagedAsync(
            branchFilter,
            request.Name,
            request.CategoryId,
            request.Active,
            request.MinPrice,
            request.MaxPrice,
            request.Page,
            request.PageSize,
            request.SortBy,
            request.SortOrder);

        var productDtos = _mapper.Map<List<ProductDto>>(pagedProducts.Items);

        return new PagedResult<ProductDto>
        {
            Items = productDtos,
            TotalCount = pagedProducts.TotalCount,
            Page = pagedProducts.Page,
            PageSize = pagedProducts.PageSize,
            TotalPages = pagedProducts.TotalPages
        };
    }
}
