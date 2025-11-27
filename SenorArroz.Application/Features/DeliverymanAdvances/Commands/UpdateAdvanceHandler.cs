using AutoMapper;
using MediatR;
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

    public UpdateAdvanceHandler(
        IDeliverymanAdvanceRepository advanceRepository,
        IMapper mapper,
        ICurrentUser currentUser)
    {
        _advanceRepository = advanceRepository;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<DeliverymanAdvanceDto> Handle(UpdateAdvanceCommand request, CancellationToken cancellationToken)
    {
        // 1. Validar que el advance existe
        var advance = await _advanceRepository.GetByIdAsync(request.Id);
        if (advance == null)
            throw new BusinessException("El abono no existe");

        // 2. Validar acceso a sucursal
        if (_currentUser.Role != "superadmin" && advance.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para editar abonos de esta sucursal");

        // 3. Validar que solo se puede editar el día de creación
        if (advance.CreatedAt.Date != DateTime.UtcNow.Date)
            throw new BusinessException("Solo se pueden editar abonos del día actual");

        // 4. Validar monto > 0
        if (request.Advance.Amount <= 0)
            throw new BusinessException("El monto debe ser mayor a cero");

        // Actualizar
        advance.Amount = request.Advance.Amount;
        advance.Notes = request.Advance.Notes;

        var updated = await _advanceRepository.UpdateAsync(advance);
        return _mapper.Map<DeliverymanAdvanceDto>(updated);
    }
}

