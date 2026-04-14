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
    private readonly IClock _clock;

    public UnlockDeliverymanDayHandler(
        IApplicationDbContext context,
        IUserRepository userRepository,
        ICurrentUser currentUser,
        IClock clock)
    {
        _context = context;
        _userRepository = userRepository;
        _currentUser = currentUser;
        _clock = clock;
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

        var branchId = Roles.IsSuperadmin(_currentUser.Role)
            ? deliveryman.BranchId
            : _currentUser.BranchId;
        if (!Roles.IsSuperadmin(_currentUser.Role) && deliveryman.BranchId != branchId)
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
        state.UnlockedAt = _clock.UtcNow;
        state.UnlockedById = _currentUser.Id;

        await _context.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
