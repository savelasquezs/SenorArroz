using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.ExpenseHeaders.Commands;

public class DeleteExpenseHeaderHandler : IRequestHandler<DeleteExpenseHeaderCommand, bool>
{
    private readonly IExpenseHeaderRepository _expenseHeaderRepository;
    private readonly ICurrentUser _currentUser;
    private readonly IBranchContext _branchContext;

    public DeleteExpenseHeaderHandler(
        IExpenseHeaderRepository expenseHeaderRepository,
        ICurrentUser currentUser,
        IBranchContext branchContext)
    {
        _expenseHeaderRepository = expenseHeaderRepository;
        _currentUser = currentUser;
        _branchContext = branchContext;
    }

    public async Task<bool> Handle(DeleteExpenseHeaderCommand request, CancellationToken cancellationToken)
    {
        var expenseHeader = await _expenseHeaderRepository.GetByIdAsync(request.Id, cancellationToken);

        if (expenseHeader == null)
        {
            throw new NotFoundException($"Gasto con ID {request.Id} no encontrado");
        }
        _branchContext.EnsureAccess(expenseHeader.BranchId);

        // Validar acceso
        if (!Roles.IsSuperadmin(_currentUser.Role))
        {
            if (expenseHeader.BranchId != _currentUser.BranchId)
            {
                throw new BusinessException("No tienes acceso a este gasto");
            }

            if (Roles.IsCashier(_currentUser.Role) && expenseHeader.CreatedById != _currentUser.Id)
            {
                throw new BusinessException("Solo puedes eliminar tus propios gastos");
            }
        }

        return await _expenseHeaderRepository.DeleteAsync(request.Id, cancellationToken);
    }
}

