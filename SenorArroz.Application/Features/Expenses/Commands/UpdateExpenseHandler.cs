// SenorArroz.Application/Features/Expenses/Commands/UpdateExpenseHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Expenses.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Expenses.Commands;

public class UpdateExpenseHandler : IRequestHandler<UpdateExpenseCommand, ExpenseDto>
{
    private readonly IExpenseRepository _expenseRepository;
    private readonly IExpenseCategoryRepository _categoryRepository;
    private readonly ICurrentUser _currentUser;
    private readonly IMapper _mapper;

    public UpdateExpenseHandler(
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

    public async Task<ExpenseDto> Handle(UpdateExpenseCommand request, CancellationToken cancellationToken)
    {
        // Validate permissions
        if (_currentUser.Role != "admin" && _currentUser.Role != "superadmin")
        {
            throw new BusinessException("No tienes permisos para modificar gastos");
        }

        var expense = await _expenseRepository.GetByIdAsync(request.Id);
        if (expense == null)
        {
            throw new NotFoundException($"Gasto con ID {request.Id} no encontrado");
        }

        // Validate category exists
        if (!await _categoryRepository.ExistsAsync(request.CategoryId))
        {
            throw new NotFoundException($"Categoría con ID {request.CategoryId} no encontrada");
        }

        // Validate name doesn't exist in this category for other expenses
        if (await _expenseRepository.NameExistsInCategoryAsync(request.Name, request.CategoryId, request.Id))
        {
            throw new BusinessException($"Ya existe otro gasto con el nombre '{request.Name}' en esta categoría");
        }

        // Update expense
        expense.Name = request.Name.Trim();
        expense.CategoryId = request.CategoryId;
        expense.Unit = request.Unit;

        expense = await _expenseRepository.UpdateAsync(expense);

        return _mapper.Map<ExpenseDto>(expense);
    }
}


