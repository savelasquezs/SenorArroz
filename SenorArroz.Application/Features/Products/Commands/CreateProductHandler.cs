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

        var product = new Product
        {
            CategoryId = request.CategoryId,
            Name = request.Name,
            Price = request.Price,
            Stock = request.Stock,
            WeightGrams = request.WeightGrams,
            Active = request.Active
        };

        var createdProduct = await _productRepository.CreateAsync(product, cancellationToken);
        return _mapper.Map<ProductDto>(createdProduct);
    }
}
