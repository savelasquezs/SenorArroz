// SenorArroz.Application/Features/Products/Queries/GetProductCategoryByIdHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Products.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Products.Queries;

public class GetProductCategoryByIdHandler : IRequestHandler<GetProductCategoryByIdQuery, ProductCategoryDto?>
{
    private readonly IProductCategoryRepository _categoryRepository;
    private readonly ICurrentUser _currentUser;
    private readonly IMapper _mapper;

    public GetProductCategoryByIdHandler(
        IProductCategoryRepository categoryRepository,
        ICurrentUser currentUser,
        IMapper mapper)
    {
        _categoryRepository = categoryRepository;
        _currentUser = currentUser;
        _mapper = mapper;
    }

    public async Task<ProductCategoryDto?> Handle(GetProductCategoryByIdQuery request, CancellationToken cancellationToken)
    {
        var category = await _categoryRepository.GetByIdAsync(request.Id);
        if (category == null)
            return null;

        // Validate access: admin can only see categories from their branch
        if (_currentUser.Role != "superadmin" && category.BranchId != _currentUser.BranchId)
        {
            throw new BusinessException("No tienes permisos para acceder a esta categoría");
        }

        var categoryDto = _mapper.Map<ProductCategoryDto>(category);

        // Add statistics
        categoryDto.TotalProducts = await _categoryRepository.GetTotalProductsAsync(category.Id);
        categoryDto.ActiveProducts = await _categoryRepository.GetActiveProductsAsync(category.Id);

        return categoryDto;
    }
}