// SenorArroz.Application/Features/Products/Commands/UpdateProductHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Products.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Products.Commands;

public class UpdateProductHandler : IRequestHandler<UpdateProductCommand, ProductDto>
{
    private readonly IProductRepository _productRepository;
    private readonly IProductCategoryRepository _categoryRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public UpdateProductHandler(
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

    public async Task<ProductDto> Handle(UpdateProductCommand request, CancellationToken cancellationToken)
    {
        // Validate product exists
        var existingProduct = await _productRepository.GetByIdAsync(request.Id);
        if (existingProduct == null)
            throw new BusinessException("El producto especificado no existe");

        // Check if user has access to this product's branch
        if (_currentUser.Role != "superadmin" && existingProduct.Category.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para modificar este producto");

        // Validate category exists
        var category = await _categoryRepository.GetByIdAsync(request.CategoryId);
        if (category == null)
            throw new BusinessException("La categoría especificada no existe");

        // Check if user has access to the new category's branch
        if (_currentUser.Role != "superadmin" && category.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para asignar productos a esta categoría");

        // Check if product name already exists in this category (excluding current product)
        if (await _productRepository.NameExistsInCategoryAsync(request.Name, request.CategoryId, request.Id))
            throw new BusinessException("Ya existe un producto con este nombre en la categoría especificada");

        // Update product properties
        existingProduct.CategoryId = request.CategoryId;
        existingProduct.Name = request.Name;
        existingProduct.Price = request.Price;
        existingProduct.Stock = request.Stock;
        existingProduct.Active = request.Active;

        var updatedProduct = await _productRepository.UpdateAsync(existingProduct);
        return _mapper.Map<ProductDto>(updatedProduct);
    }
}
