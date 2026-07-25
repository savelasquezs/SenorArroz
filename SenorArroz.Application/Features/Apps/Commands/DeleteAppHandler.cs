// SenorArroz.Application/Features/Apps/Commands/DeleteAppHandler.cs
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Apps.Commands;

public class DeleteAppHandler : IRequestHandler<DeleteAppCommand, bool>
{
    private readonly IAppRepository _appRepository;
    private readonly ICurrentUser _currentUser;
    private readonly IBranchContext _branchContext;

    public DeleteAppHandler(IAppRepository appRepository, ICurrentUser currentUser, IBranchContext branchContext)
    {
        _appRepository = appRepository;
        _currentUser = currentUser;
        _branchContext = branchContext;
    }

    public async Task<bool> Handle(DeleteAppCommand request, CancellationToken cancellationToken)
    {
        // Validate app exists
        var existingApp = await _appRepository.GetByIdAsync(request.Id, cancellationToken);
        if (existingApp == null)
            return false;
        _branchContext.EnsureAccess(existingApp.Bank.BranchId);

        // Check if user has access to this app's branch
        if (!Roles.IsSuperadmin(_currentUser.Role) && existingApp.Bank.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para eliminar esta app");

        return await _appRepository.DeleteAsync(request.Id, cancellationToken);
    }
}
