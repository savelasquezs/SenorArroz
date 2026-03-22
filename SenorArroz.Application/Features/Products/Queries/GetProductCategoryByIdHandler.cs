// SenorArroz.Application/Features/Products/Queries/GetProductCategoryByIdHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.Products.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Products.Queries;

public class GetProductCategoryByIdHandler : IRequestHandler<GetProductCategoryByIdQuery, ProductCategoryDto?>
{
    private readonly IProductCategoryRepository _categoryRepository;
    private readonly IMapper _mapper;

    public GetProductCategoryByIdHandler(
        IProductCategoryRepository categoryRepository,
        IMapper mapper)
    {
        _categoryRepository = categoryRepository;
        _mapper = mapper;
    }

    public async Task<ProductCategoryDto?> Handle(GetProductCategoryByIdQuery request, CancellationToken cancellationToken)
    {
        var category = await _categoryRepository.GetByIdAsync(request.Id);
        if (category == null)
            return null;

        var categoryDto = _mapper.Map<ProductCategoryDto>(category);

        // Add statistics
        categoryDto.TotalProducts = await _categoryRepository.GetTotalProductsAsync(category.Id);
        categoryDto.ActiveProducts = await _categoryRepository.GetActiveProductsAsync(category.Id);

        return categoryDto;
    }
}