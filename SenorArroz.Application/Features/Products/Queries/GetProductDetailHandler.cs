// SenorArroz.Application/Features/Products/Queries/GetProductDetailHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Products.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Products.Queries;

public class GetProductDetailHandler : IRequestHandler<GetProductDetailQuery, ProductDetailDto?>
{
    private readonly IProductRepository _productRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public GetProductDetailHandler(IProductRepository productRepository, IMapper mapper, ICurrentUser currentUser)
    {
        _productRepository = productRepository;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<ProductDetailDto?> Handle(GetProductDetailQuery request, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdWithStatisticsAsync(request.Id);
        
        if (product == null)
            return null;

        // Check if user has access to this product's branch
        if (_currentUser.Role != "superadmin" && product.Category.BranchId != _currentUser.BranchId)
            return null;

        var productDetailDto = _mapper.Map<ProductDetailDto>(product);

        // Add statistical data
        productDetailDto.TotalSales = await _productRepository.GetTotalSalesAsync(product.Id);
        productDetailDto.TotalRevenue = await _productRepository.GetTotalRevenueAsync(product.Id);
        productDetailDto.TotalOrders = await _productRepository.GetTotalOrdersAsync(product.Id);
        productDetailDto.TotalCustomers = await _productRepository.GetTotalCustomersAsync(product.Id);
        productDetailDto.LastSoldAt = await _productRepository.GetLastSoldAtAsync(product.Id);

        return productDetailDto;
    }
}
