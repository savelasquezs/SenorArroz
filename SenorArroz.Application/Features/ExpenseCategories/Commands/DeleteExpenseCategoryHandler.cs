// SenorArroz.Application/Features/ExpenseCategories/Commands/DeleteExpenseCategoryHandler.cs
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.ExpenseCategories.Commands;

public class DeleteExpenseCategoryHandler : IRequestHandler<DeleteExpenseCategoryCommand, bool>
{
    private readonly IExpenseCategoryRepository _categoryRepository;
    private readonly ICurrentUser _currentUser;

    public DeleteExpenseCategoryHandler(
        IExpenseCategoryRepository categoryRepository,
        ICurrentUser currentUser)
    {
        _categoryRepository = categoryRepository;
        _currentUser = currentUser;
    }

    public async Task<bool> Handle(DeleteExpenseCategoryCommand request, CancellationToken cancellationToken)
    {
        // Validate permissions
        if (_currentUser.Role != "admin" && _currentUser.Role != "superadmin")
        {
            throw new BusinessException("No tienes permisos para eliminar categorías de gastos");
        }

        var category = await _categoryRepository.GetByIdAsync(request.Id);
        if (category == null)
        {
            throw new NotFoundException($"Categoría con ID {request.Id} no encontrada");
        }

        // Check if category has expenses
        var categoryWithExpenses = await _categoryRepository.GetByIdWithExpensesAsync(request.Id);
        if (categoryWithExpenses != null && categoryWithExpenses.Expenses.Any())
        {
            throw new BusinessException("No se puede eliminar una categoría que tiene gastos asociados");
        }

        return await _categoryRepository.DeleteAsync(request.Id);
    }
}


