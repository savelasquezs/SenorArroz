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
        var product = await _productRepository.GetByIdWithStatisticsAsync(request.Id, cancellationToken);
        
        if (product == null)
            return null;

        var productDetailDto = _mapper.Map<ProductDetailDto>(product);

        // Add statistical data
        productDetailDto.TotalSales = await _productRepository.GetTotalSalesAsync(product.Id, cancellationToken);
        productDetailDto.TotalRevenue = await _productRepository.GetTotalRevenueAsync(product.Id, cancellationToken);
        productDetailDto.TotalOrders = await _productRepository.GetTotalOrdersAsync(product.Id, cancellationToken);
        productDetailDto.TotalCustomers = await _productRepository.GetTotalCustomersAsync(product.Id, cancellationToken);
        productDetailDto.LastSoldAt = await _productRepository.GetLastSoldAtAsync(product.Id, cancellationToken);

        const int salesChartDays = 90;
        var evolution = await _productRepository.GetSalesUnitsEvolutionByProductAsync(
            product.Id,
            DateTime.UtcNow.Date,
            salesChartDays,
            cancellationToken);
        productDetailDto.SalesUnitsEvolution = evolution
            .Select(e => new ProductSalesUnitsEvolutionPointDto
            {
                BucketStart = e.BucketDate,
                UnitsSold = e.UnitsSold
            })
            .ToList();

        return productDetailDto;
    }
}
