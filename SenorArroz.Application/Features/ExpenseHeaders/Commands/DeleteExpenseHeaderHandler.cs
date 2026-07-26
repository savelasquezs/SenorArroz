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

        if (!Roles.IsAdminOrSuperadmin(_currentUser.Role))
        {
            throw new BusinessException("Solo un administrador o superadministrador puede eliminar gastos");
        }

        if (!Roles.IsSuperadmin(_currentUser.Role))
        {
            if (expenseHeader.BranchId != _currentUser.BranchId)
            {
                throw new BusinessException("No tienes acceso a este gasto");
            }
        }

        return await _expenseHeaderRepository.DeleteAsync(request.Id, cancellationToken);
    }
}

