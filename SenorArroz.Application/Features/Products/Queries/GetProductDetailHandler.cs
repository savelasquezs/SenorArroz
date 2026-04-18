// SenorArroz.Application/Features/Products/Queries/GetProductDetailHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Products.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Products.Queries;

public class GetProductDetailHandler : IRequestHandler<GetProductDetailQuery, ProductDetailDto?>
{
    private readonly IProductRepository _productRepository;
    private readonly IMapper _mapper;
    private readonly IClock _clock;

    public GetProductDetailHandler(IProductRepository productRepository, IMapper mapper, IClock clock)
    {
        _productRepository = productRepository;
        _mapper = mapper;
        _clock = clock;
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
        var endDayColombia = ColombiaTimeHelper.GetNowInColombiaFromUtc(_clock.UtcNow).Date;
        var evolution = await _productRepository.GetSalesUnitsEvolutionByProductAsync(
            product.Id,
            endDayColombia,
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
