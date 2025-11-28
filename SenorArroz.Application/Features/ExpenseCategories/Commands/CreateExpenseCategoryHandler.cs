// SenorArroz.Application/Features/ExpenseCategories/Commands/CreateExpenseCategoryHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.ExpenseCategories.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.ExpenseCategories.Commands;

public class CreateExpenseCategoryHandler : IRequestHandler<CreateExpenseCategoryCommand, ExpenseCategoryDto>
{
    private readonly IExpenseCategoryRepository _categoryRepository;
    private readonly ICurrentUser _currentUser;
    private readonly IMapper _mapper;

    public CreateExpenseCategoryHandler(
        IExpenseCategoryRepository categoryRepository,
        ICurrentUser currentUser,
        IMapper mapper)
    {
        _categoryRepository = categoryRepository;
        _currentUser = currentUser;
        _mapper = mapper;
    }

    public async Task<ExpenseCategoryDto> Handle(CreateExpenseCategoryCommand request, CancellationToken cancellationToken)
    {
        // Validate permissions: only Admin and Superadmin can create categories
        if (_currentUser.Role != "admin" && _currentUser.Role != "superadmin")
        {
            throw new BusinessException("No tienes permisos para crear categorías de gastos");
        }

        // Validate name doesn't exist
        if (await _categoryRepository.NameExistsAsync(request.Name))
        {
            throw new BusinessException($"Ya existe una categoría con el nombre '{request.Name}'");
        }

        var category = new ExpenseCategory
        {
            Name = request.Name.Trim()
        };

        category = await _categoryRepository.CreateAsync(category);

        var categoryDto = _mapper.Map<ExpenseCategoryDto>(category);

        // Initialize stats for new category
        categoryDto.TotalExpenses = 0;

        return categoryDto;
    }
}

