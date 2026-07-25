using MediatR;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.DeliverymanAdvances.Commands;

public class DeleteAdvanceHandler : IRequestHandler<DeleteAdvanceCommand, bool>
{
    private readonly IDeliverymanAdvanceRepository _advanceRepository;
    private readonly ICurrentUser _currentUser;
    private readonly IBranchContext _branchContext;
    private readonly IClock _clock;

    public DeleteAdvanceHandler(
        IDeliverymanAdvanceRepository advanceRepository,
        ICurrentUser currentUser,
        IBranchContext branchContext,
        IClock clock)
    {
        _advanceRepository = advanceRepository;
        _currentUser = currentUser;
        _branchContext = branchContext;
        _clock = clock;
    }

    public async Task<bool> Handle(DeleteAdvanceCommand request, CancellationToken cancellationToken)
    {
        // 1. Validar que existe
        var advance = await _advanceRepository.GetByIdAsync(request.Id, cancellationToken);
        if (advance == null)
            throw new BusinessException("El abono no existe");
        _branchContext.EnsureAccess(advance.BranchId);

        // 2. Validar acceso a sucursal
        if (!Roles.IsSuperadmin(_currentUser.Role) && advance.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para eliminar abonos de esta sucursal");

        // 3. Validar que solo se puede eliminar el día de creación (calendario Colombia)
        if (!ColombiaTimeHelper.IsColombiaTodayFromUtc(advance.CreatedAt, _clock.UtcNow))
            throw new BusinessException("Solo se pueden eliminar abonos del día actual (hora Colombia)");

        return await _advanceRepository.DeleteAsync(request.Id, cancellationToken);
    }
}

