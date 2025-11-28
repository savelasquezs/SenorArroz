using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.ExpenseHeaders.Commands;

public class DeleteExpenseHeaderHandler : IRequestHandler<DeleteExpenseHeaderCommand, bool>
{
    private readonly IExpenseHeaderRepository _expenseHeaderRepository;
    private readonly ICurrentUser _currentUser;

    public DeleteExpenseHeaderHandler(IExpenseHeaderRepository expenseHeaderRepository, ICurrentUser currentUser)
    {
        _expenseHeaderRepository = expenseHeaderRepository;
        _currentUser = currentUser;
    }

    public async Task<bool> Handle(DeleteExpenseHeaderCommand request, CancellationToken cancellationToken)
    {
        var expenseHeader = await _expenseHeaderRepository.GetByIdAsync(request.Id);

        if (expenseHeader == null)
        {
            throw new NotFoundException($"Gasto con ID {request.Id} no encontrado");
        }

        // Validar acceso
        if (_currentUser.Role != "superadmin")
        {
            if (expenseHeader.BranchId != _currentUser.BranchId)
            {
                throw new BusinessException("No tienes acceso a este gasto");
            }

            if (_currentUser.Role == "cashier" && expenseHeader.CreatedById != _currentUser.Id)
            {
                throw new BusinessException("Solo puedes eliminar tus propios gastos");
            }
        }

        return await _expenseHeaderRepository.DeleteAsync(request.Id);
    }
}

