// SenorArroz.Application/Features/Products/Commands/UpdateProductHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.Products.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Products.Commands;

public class UpdateProductHandler : IRequestHandler<UpdateProductCommand, ProductDto>
{
    private readonly IProductRepository _productRepository;
    private readonly IProductCategoryRepository _categoryRepository;
    private readonly IMapper _mapper;

    public UpdateProductHandler(
        IProductRepository productRepository,
        IProductCategoryRepository categoryRepository,
        IMapper mapper)
    {
        _productRepository = productRepository;
        _categoryRepository = categoryRepository;
        _mapper = mapper;
    }

    public async Task<ProductDto> Handle(UpdateProductCommand request, CancellationToken cancellationToken)
    {
        // Validate product exists
        var existingProduct = await _productRepository.GetByIdAsync(request.Id, cancellationToken);
        if (existingProduct == null)
            throw new BusinessException("El producto especificado no existe");

        // Validate category exists
        var category = await _categoryRepository.GetByIdAsync(request.CategoryId);
        if (category == null)
            throw new BusinessException("La categoría especificada no existe");

        // Check if product name already exists in this category (excluding current product)
        if (await _productRepository.NameExistsInCategoryAsync(request.Name, request.CategoryId, request.Id))
            throw new BusinessException("Ya existe un producto con este nombre en la categoría especificada");
        ValidateServings(request.ServesPeopleMin, request.ServesPeopleMax);
        if (request.CommercialProfileId.HasValue && !await _productRepository.CommercialProfileBelongsToBranchAsync(request.CommercialProfileId.Value, category.BranchId, cancellationToken))
            throw new BusinessException("La ficha comercial no pertenece a la sucursal del producto.");

        // Update product properties (Stock solo si viene en el request; si no se envía, no pisar el valor actual)
        existingProduct.CategoryId = request.CategoryId;
        existingProduct.Name = request.Name;
        existingProduct.Price = request.Price;
        if (request.Stock.HasValue)
            existingProduct.Stock = request.Stock;
        existingProduct.WeightGrams = request.WeightGrams;
        existingProduct.Active = request.Active;
        existingProduct.CommercialProfileId = request.CommercialProfileId;
        existingProduct.ServesPeopleMin = request.ServesPeopleMin;
        existingProduct.ServesPeopleMax = request.ServesPeopleMax;

        var updatedProduct = await _productRepository.UpdateAsync(existingProduct, cancellationToken);
        return _mapper.Map<ProductDto>(updatedProduct);
    }

    private static void ValidateServings(int? min, int? max)
    {
        if (min.HasValue != max.HasValue) throw new BusinessException("Debes indicar el mínimo y máximo de personas.");
        if (min <= 0) throw new BusinessException("El mínimo de personas debe ser mayor que cero.");
        if (max < min) throw new BusinessException("El máximo de personas debe ser igual o mayor que el mínimo.");
    }
}
