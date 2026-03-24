using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Deliverymen.Commands;

public class UnlockDeliverymanDayHandler : IRequestHandler<UnlockDeliverymanDayCommand, Unit>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserRepository _userRepository;
    private readonly ICurrentUser _currentUser;

    public UnlockDeliverymanDayHandler(
        IApplicationDbContext context,
        IUserRepository userRepository,
        ICurrentUser currentUser)
    {
        _context = context;
        _userRepository = userRepository;
        _currentUser = currentUser;
    }

    public async Task<Unit> Handle(UnlockDeliverymanDayCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Date) || !DateOnly.TryParse(request.Date, out var day))
            throw new BusinessException("Fecha inválida (use YYYY-MM-DD)");

        var deliveryman = await _userRepository.GetByIdAsync(request.DeliverymanId, cancellationToken);
        if (deliveryman == null)
            throw new BusinessException("El domiciliario no existe");
        if (deliveryman.Role != UserRole.Deliveryman)
            throw new BusinessException("El usuario no es un domiciliario");

        var branchId = _currentUser.Role == "superadmin"
            ? deliveryman.BranchId
            : _currentUser.BranchId;
        if (_currentUser.Role != "superadmin" && deliveryman.BranchId != branchId)
            throw new BusinessException("No tienes permisos");

        var state = await _context.DeliverymanDayStates
            .FirstOrDefaultAsync(
                s => s.BranchId == branchId
                     && s.DeliverymanId == request.DeliverymanId
                     && s.Date == day,
                cancellationToken);

        if (state == null)
            throw new BusinessException("No hay liquidación registrada para este día");

        state.Blocked = false;
        state.UnlockedAt = DateTime.UtcNow;
        state.UnlockedById = _currentUser.Id;

        await _context.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
