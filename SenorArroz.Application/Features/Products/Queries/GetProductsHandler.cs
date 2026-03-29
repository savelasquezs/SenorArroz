// SenorArroz.Application/Features/Products/Queries/GetProductsHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.Products.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.Products.Queries;

public class GetProductsHandler : IRequestHandler<GetProductsQuery, PagedResult<ProductDto>>
{
    private readonly IProductRepository _productRepository;
    private readonly IMapper _mapper;

    public GetProductsHandler(IProductRepository productRepository, IMapper mapper)
    {
        _productRepository = productRepository;
        _mapper = mapper;
    }

    public async Task<PagedResult<ProductDto>> Handle(GetProductsQuery request, CancellationToken cancellationToken)
    {
        // Catálogo transversal: el mismo menú de productos aplica a todas las sucursales.
        // No se deriva sucursal del token; BranchId en la query es opcional (p. ej. vistas admin/filtrado explícito).
        int? branchFilter = request.BranchId.HasValue && request.BranchId.Value > 0
            ? request.BranchId.Value
            : null;

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
