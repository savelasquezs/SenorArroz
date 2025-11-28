// SenorArroz.Application/Features/Expenses/Commands/CreateExpenseHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Expenses.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Expenses.Commands;

public class CreateExpenseHandler : IRequestHandler<CreateExpenseCommand, ExpenseDto>
{
    private readonly IExpenseRepository _expenseRepository;
    private readonly IExpenseCategoryRepository _categoryRepository;
    private readonly ICurrentUser _currentUser;
    private readonly IMapper _mapper;

    public CreateExpenseHandler(
        IExpenseRepository expenseRepository,
        IExpenseCategoryRepository categoryRepository,
        ICurrentUser currentUser,
        IMapper mapper)
    {
        _expenseRepository = expenseRepository;
        _categoryRepository = categoryRepository;
        _currentUser = currentUser;
        _mapper = mapper;
    }

    public async Task<ExpenseDto> Handle(CreateExpenseCommand request, CancellationToken cancellationToken)
    {
        // Validate permissions: only Admin and Superadmin can create expenses
        if (_currentUser.Role != "admin" && _currentUser.Role != "superadmin")
        {
            throw new BusinessException("No tienes permisos para crear gastos");
        }

        // Validate category exists
        if (!await _categoryRepository.ExistsAsync(request.CategoryId))
        {
            throw new NotFoundException($"Categoría con ID {request.CategoryId} no encontrada");
        }

        // Validate name doesn't exist in this category
        if (await _expenseRepository.NameExistsInCategoryAsync(request.Name, request.CategoryId))
        {
            throw new BusinessException($"Ya existe un gasto con el nombre '{request.Name}' en esta categoría");
        }

        var expense = new Expense
        {
            Name = request.Name.Trim(),
            CategoryId = request.CategoryId,
            Unit = request.Unit
        };

        expense = await _expenseRepository.CreateAsync(expense);

        return _mapper.Map<ExpenseDto>(expense);
    }
}

