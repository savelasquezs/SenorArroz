// SenorArroz.Application/Features/Expenses/Commands/DeleteExpenseHandler.cs
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Expenses.Commands;

public class DeleteExpenseHandler : IRequestHandler<DeleteExpenseCommand, bool>
{
    private readonly IExpenseRepository _expenseRepository;
    private readonly ICurrentUser _currentUser;

    public DeleteExpenseHandler(
        IExpenseRepository expenseRepository,
        ICurrentUser currentUser)
    {
        _expenseRepository = expenseRepository;
        _currentUser = currentUser;
    }

    public async Task<bool> Handle(DeleteExpenseCommand request, CancellationToken cancellationToken)
    {
        // Validate permissions
        if (_currentUser.Role != "admin" && _currentUser.Role != "superadmin")
        {
            throw new BusinessException("No tienes permisos para eliminar gastos");
        }

        var expense = await _expenseRepository.GetByIdAsync(request.Id);
        if (expense == null)
        {
            throw new NotFoundException($"Gasto con ID {request.Id} no encontrado");
        }

        // The repository will check if expense is used in expense details
        var result = await _expenseRepository.DeleteAsync(request.Id);
        
        if (!result)
        {
            throw new BusinessException("No se puede eliminar un gasto que está siendo utilizado en facturas de gastos");
        }

        return result;
    }
}


