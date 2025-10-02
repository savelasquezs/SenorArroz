// SenorArroz.Application/Features/Products/Commands/CreateProductCategoryHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Products.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Products.Commands;

public class CreateProductCategoryHandler : IRequestHandler<CreateProductCategoryCommand, ProductCategoryDto>
{
    private readonly IProductCategoryRepository _categoryRepository;
    private readonly IBranchRepository _branchRepository;
    private readonly ICurrentUser _currentUser;
    private readonly IMapper _mapper;

    public CreateProductCategoryHandler(
        IProductCategoryRepository categoryRepository,
        IBranchRepository branchRepository,
        ICurrentUser currentUser,
        IMapper mapper)
    {
        _categoryRepository = categoryRepository;
        _branchRepository = branchRepository;
        _currentUser = currentUser;
        _mapper = mapper;
    }

    public async Task<ProductCategoryDto> Handle(CreateProductCategoryCommand request, CancellationToken cancellationToken)
    {
        // Determine branch
        int branchId;

        if (_currentUser.Role == "superadmin")
        {
            // Superadmin can specify branch or needs to provide it
            if (!request.BranchId.HasValue || request.BranchId <= 0)
            {
                throw new BusinessException("Superadmin debe especificar la sucursal");
            }
            branchId = request.BranchId.Value;
        }
        else if (_currentUser.Role == "admin")
        {
            // Admin uses their branch
            branchId = _currentUser.BranchId;
        }
        else
        {
            throw new BusinessException("No tienes permisos para crear categorías");
        }

        // Validate branch exists
        if (!await _branchRepository.ExistsAsync(branchId))
        {
            throw new NotFoundException($"Sucursal con ID {branchId} no encontrada");
        }

        // Validate name doesn't exist in this branch
        if (await _categoryRepository.NameExistsInBranchAsync(request.Name, branchId))
        {
            throw new BusinessException($"Ya existe una categoría con el nombre '{request.Name}' en esta sucursal");
        }

        var category = new ProductCategory
        {
            BranchId = branchId,
            Name = request.Name.Trim()
        };

        category = await _categoryRepository.CreateAsync(category);

        var categoryDto = _mapper.Map<ProductCategoryDto>(category);

        // Initialize stats for new category
        categoryDto.TotalProducts = 0;
        categoryDto.ActiveProducts = 0;

        return categoryDto;
    }
}