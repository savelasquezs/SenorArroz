// SenorArroz.Application/Features/Products/Commands/UpdateProductCategoryHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.Products.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Products.Commands;

public class UpdateProductCategoryHandler : IRequestHandler<UpdateProductCategoryCommand, ProductCategoryDto>
{
    private readonly IProductCategoryRepository _categoryRepository;
    private readonly IMapper _mapper;

    public UpdateProductCategoryHandler(
        IProductCategoryRepository categoryRepository,
        IMapper mapper)
    {
        _categoryRepository = categoryRepository;
        _mapper = mapper;
    }

    public async Task<ProductCategoryDto> Handle(UpdateProductCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await _categoryRepository.GetByIdAsync(request.Id, cancellationToken);
        if (category == null)
        {
            throw new NotFoundException($"Categoría con ID {request.Id} no encontrada");
        }

        // Validate name doesn't exist for other categories in the same branch
        if (await _categoryRepository.NameExistsInBranchAsync(request.Name, category.BranchId, request.Id))
        {
            throw new BusinessException($"Ya existe otra categoría con el nombre '{request.Name}' en esta sucursal");
        }

        // Update category
        category.Name = request.Name.Trim();
        category.StorefrontRole = request.StorefrontRole;

        category = await _categoryRepository.UpdateAsync(category, cancellationToken);

        var categoryDto = _mapper.Map<ProductCategoryDto>(category);

        // Add current statistics
        categoryDto.TotalProducts = await _categoryRepository.GetTotalProductsAsync(category.Id, cancellationToken);
        categoryDto.ActiveProducts = await _categoryRepository.GetActiveProductsAsync(category.Id, cancellationToken);

        return categoryDto;
    }
}
