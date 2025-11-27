using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.DeliverymanAdvances.Commands;

public class DeleteAdvanceHandler : IRequestHandler<DeleteAdvanceCommand, bool>
{
    private readonly IDeliverymanAdvanceRepository _advanceRepository;
    private readonly ICurrentUser _currentUser;

    public DeleteAdvanceHandler(
        IDeliverymanAdvanceRepository advanceRepository,
        ICurrentUser currentUser)
    {
        _advanceRepository = advanceRepository;
        _currentUser = currentUser;
    }

    public async Task<bool> Handle(DeleteAdvanceCommand request, CancellationToken cancellationToken)
    {
        // 1. Validar que existe
        var advance = await _advanceRepository.GetByIdAsync(request.Id);
        if (advance == null)
            throw new BusinessException("El abono no existe");

        // 2. Validar acceso a sucursal
        if (_currentUser.Role != "superadmin" && advance.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para eliminar abonos de esta sucursal");

        // 3. Validar que solo se puede eliminar el día de creación
        if (advance.CreatedAt.Date != DateTime.UtcNow.Date)
            throw new BusinessException("Solo se pueden eliminar abonos del día actual");

        return await _advanceRepository.DeleteAsync(request.Id);
    }
}

