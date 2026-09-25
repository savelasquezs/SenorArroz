using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.ExpenseHeaders.Commands;

public class DeleteExpenseHeaderHandler : IRequestHandler<DeleteExpenseHeaderCommand, bool>
{
    private readonly IExpenseHeaderRepository _expenseHeaderRepository;
    private readonly ICurrentUser _currentUser;
    private readonly IBranchContext _branchContext;
    private readonly IApplicationDbContext _db;
    private readonly IInventoryService _inventory;

    public DeleteExpenseHeaderHandler(
        IExpenseHeaderRepository expenseHeaderRepository,
        ICurrentUser currentUser,
        IBranchContext branchContext,
        IApplicationDbContext db,
        IInventoryService inventory)
    {
        _expenseHeaderRepository = expenseHeaderRepository;
        _currentUser = currentUser;
        _branchContext = branchContext;
        _db = db;
        _inventory = inventory;
    }

    public async Task<bool> Handle(DeleteExpenseHeaderCommand request, CancellationToken cancellationToken)
    {
        var expenseHeader = await _expenseHeaderRepository.GetByIdWithDetailsAsync(request.Id, cancellationToken);

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

        await using var transaction = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync(cancellationToken) : null;
        try
        {
            await _inventory.ReversePurchaseAsync(expenseHeader, $"expense-header:{expenseHeader.Id}:delete", cancellationToken);
            var deleted = await _expenseHeaderRepository.DeleteAsync(request.Id, cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return deleted;
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}

