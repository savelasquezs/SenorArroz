// SenorArroz.Application/Features/Products/Queries/GetProductDetailHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.Products.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Products.Queries;

public class GetProductDetailHandler : IRequestHandler<GetProductDetailQuery, ProductDetailDto?>
{
    private readonly IProductRepository _productRepository;
    private readonly IMapper _mapper;

    public GetProductDetailHandler(IProductRepository productRepository, IMapper mapper)
    {
        _productRepository = productRepository;
        _mapper = mapper;
    }

    public async Task<ProductDetailDto?> Handle(GetProductDetailQuery request, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdWithStatisticsAsync(request.Id);
        
        if (product == null)
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
