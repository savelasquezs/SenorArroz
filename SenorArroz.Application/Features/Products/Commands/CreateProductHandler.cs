// SenorArroz.Application/Features/Products/Commands/CreateProductHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
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
    private readonly ICurrentUser _currentUser;

    public CreateProductHandler(
        IProductRepository productRepository,
        IProductCategoryRepository categoryRepository,
        IMapper mapper,
        ICurrentUser currentUser)
    {
        _productRepository = productRepository;
        _categoryRepository = categoryRepository;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<ProductDto> Handle(CreateProductCommand request, CancellationToken cancellationToken)
    {
        // Validate category exists
        var category = await _categoryRepository.GetByIdAsync(request.CategoryId);
        if (category == null)
            throw new BusinessException("La categoría especificada no existe");

        // Check if user has access to this category's branch
        if (_currentUser.Role != "superadmin" && category.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para crear productos en esta categoría");

        // Check if product name already exists in this category
        if (await _productRepository.NameExistsInCategoryAsync(request.Name, request.CategoryId))
            throw new BusinessException("Ya existe un producto con este nombre en la categoría especificada");

        var product = new Product
        {
            CategoryId = request.CategoryId,
            Name = request.Name,
            Price = request.Price,
            Stock = request.Stock,
            Active = request.Active
        };

        var createdProduct = await _productRepository.CreateAsync(product);
        return _mapper.Map<ProductDto>(createdProduct);
    }
}
