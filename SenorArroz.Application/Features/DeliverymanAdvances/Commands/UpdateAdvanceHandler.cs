using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.DeliverymanAdvances.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.DeliverymanAdvances.Commands;

public class UpdateAdvanceHandler : IRequestHandler<UpdateAdvanceCommand, DeliverymanAdvanceDto>
{
    private readonly IDeliverymanAdvanceRepository _advanceRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public UpdateAdvanceHandler(
        IDeliverymanAdvanceRepository advanceRepository,
        IMapper mapper,
        ICurrentUser currentUser,
        IClock clock)
    {
        _advanceRepository = advanceRepository;
        _mapper = mapper;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<DeliverymanAdvanceDto> Handle(UpdateAdvanceCommand request, CancellationToken cancellationToken)
    {
        // 1. Validar que el advance existe
        var advance = await _advanceRepository.GetByIdAsync(request.Id, cancellationToken);
        if (advance == null)
            throw new BusinessException("El abono no existe");

        // 2. Validar acceso a sucursal
        if (!Roles.IsSuperadmin(_currentUser.Role) && advance.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para editar abonos de esta sucursal");

        // 3. Validar que solo se puede editar el día de creación (calendario Colombia)
        if (!ColombiaTimeHelper.IsColombiaTodayFromUtc(advance.CreatedAt, _clock.UtcNow))
            throw new BusinessException("Solo se pueden editar abonos del día actual (hora Colombia)");

        // 4. Validar monto > 0
        if (request.Advance.Amount <= 0)
            throw new BusinessException("El monto debe ser mayor a cero");

        // Actualizar
        advance.Amount = request.Advance.Amount;
        advance.Notes = request.Advance.Notes;

        var updated = await _advanceRepository.UpdateAsync(advance, cancellationToken);
        return _mapper.Map<DeliverymanAdvanceDto>(updated);
    }
}

