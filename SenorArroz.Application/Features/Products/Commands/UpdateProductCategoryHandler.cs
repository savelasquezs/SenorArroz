// SenorArroz.Application/Features/Products/Commands/UpdateProductCategoryHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Products.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Products.Commands;

public class UpdateProductCategoryHandler : IRequestHandler<UpdateProductCategoryCommand, ProductCategoryDto>
{
    private readonly IProductCategoryRepository _categoryRepository;
    private readonly ICurrentUser _currentUser;
    private readonly IMapper _mapper;

    public UpdateProductCategoryHandler(
        IProductCategoryRepository categoryRepository,
        ICurrentUser currentUser,
        IMapper mapper)
    {
        _categoryRepository = categoryRepository;
        _currentUser = currentUser;
        _mapper = mapper;
    }

    public async Task<ProductCategoryDto> Handle(UpdateProductCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await _categoryRepository.GetByIdAsync(request.Id);
        if (category == null)
        {
            throw new NotFoundException($"Categoría con ID {request.Id} no encontrada");
        }

        // Validate access: admin can only modify categories from their branch
        if (_currentUser.Role != "superadmin" && category.BranchId != _currentUser.BranchId)
        {
            throw new BusinessException("No tienes permisos para modificar esta categoría");
        }

        // Validate name doesn't exist for other categories in the same branch
        if (await _categoryRepository.NameExistsInBranchAsync(request.Name, category.BranchId, request.Id))
        {
            throw new BusinessException($"Ya existe otra categoría con el nombre '{request.Name}' en esta sucursal");
        }

        // Update category
        category.Name = request.Name.Trim();

        category = await _categoryRepository.UpdateAsync(category);

        var categoryDto = _mapper.Map<ProductCategoryDto>(category);

        // Add current statistics
        categoryDto.TotalProducts = await _categoryRepository.GetTotalProductsAsync(category.Id);
        categoryDto.ActiveProducts = await _categoryRepository.GetActiveProductsAsync(category.Id);

        return categoryDto;
    }
}