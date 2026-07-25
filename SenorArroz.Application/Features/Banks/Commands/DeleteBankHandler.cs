// SenorArroz.Application/Features/Banks/Commands/DeleteBankHandler.cs
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Banks.Commands;

public class DeleteBankHandler : IRequestHandler<DeleteBankCommand, bool>
{
    private readonly IBankRepository _bankRepository;
    private readonly ICurrentUser _currentUser;
    private readonly IBranchContext _branchContext;

    public DeleteBankHandler(IBankRepository bankRepository, ICurrentUser currentUser, IBranchContext branchContext)
    {
        _bankRepository = bankRepository;
        _currentUser = currentUser;
        _branchContext = branchContext;
    }

    public async Task<bool> Handle(DeleteBankCommand request, CancellationToken cancellationToken)
    {
        // Validate bank exists
        var existingBank = await _bankRepository.GetByIdAsync(request.Id, cancellationToken);
        if (existingBank == null)
            return false;
        _branchContext.EnsureAccess(existingBank.BranchId);

        // Check if user has access to this bank's branch
        if (!Roles.IsSuperadmin(_currentUser.Role) && existingBank.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para eliminar este banco");

        return await _bankRepository.DeleteAsync(request.Id, cancellationToken);
    }
}
