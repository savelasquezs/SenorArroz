// SenorArroz.Application/Features/Products/Commands/CreateProductHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.Products.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Products.Commands;

public class CreateProductHandler : IRequestHandler<CreateProductCommand, ProductDto>
{
    private readonly IProductRepository _productRepository;
    private readonly IProductCategoryRepository _categoryRepository;
    private readonly IMapper _mapper;

    public CreateProductHandler(
        IProductRepository productRepository,
        IProductCategoryRepository categoryRepository,
        IMapper mapper)
    {
        _productRepository = productRepository;
        _categoryRepository = categoryRepository;
        _mapper = mapper;
    }

    public async Task<ProductDto> Handle(CreateProductCommand request, CancellationToken cancellationToken)
    {
        // Validate category exists
        var category = await _categoryRepository.GetByIdAsync(request.CategoryId, cancellationToken);
        if (category == null)
            throw new BusinessException("La categoría especificada no existe");

        // Check if product name already exists in this category
        if (await _productRepository.NameExistsInCategoryAsync(request.Name, request.CategoryId))
            throw new BusinessException("Ya existe un producto con este nombre en la categoría especificada");
        ValidateServings(request.ServesPeopleMin, request.ServesPeopleMax);
        if (request.CommercialProfileId.HasValue && !await _productRepository.CommercialProfileBelongsToBranchAsync(request.CommercialProfileId.Value, category.BranchId, cancellationToken))
            throw new BusinessException("La ficha comercial no pertenece a la sucursal del producto.");

        var product = new Product
        {
            CategoryId = request.CategoryId,
            Name = request.Name,
            Price = request.Price,
            Stock = request.Stock,
            WeightGrams = request.WeightGrams,
            Active = request.Active,
            CommercialProfileId = request.CommercialProfileId,
            ServesPeopleMin = request.ServesPeopleMin,
            ServesPeopleMax = request.ServesPeopleMax,
            StorefrontVariantLabel = Clean(request.StorefrontVariantLabel),
            StorefrontSortOrder = request.StorefrontSortOrder
        };

        var createdProduct = await _productRepository.CreateAsync(product, cancellationToken);
        return _mapper.Map<ProductDto>(createdProduct);
    }

    private static void ValidateServings(int? min, int? max)
    {
        if (min.HasValue != max.HasValue) throw new BusinessException("Debes indicar el mínimo y máximo de personas.");
        if (min <= 0) throw new BusinessException("El mínimo de personas debe ser mayor que cero.");
        if (max < min) throw new BusinessException("El máximo de personas debe ser igual o mayor que el mínimo.");
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
