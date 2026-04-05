using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Deliverymen.DTOs;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Deliverymen.Queries;

public class GetMyDeliverymanDayStateHandler : IRequestHandler<GetMyDeliverymanDayStateQuery, MyDeliverymanDayStateDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IUserRepository _userRepository;

    public GetMyDeliverymanDayStateHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IUserRepository userRepository)
    {
        _db = db;
        _currentUser = currentUser;
        _userRepository = userRepository;
    }

    public async Task<MyDeliverymanDayStateDto> Handle(GetMyDeliverymanDayStateQuery request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
            throw new BusinessException("Usuario no autenticado");

        if (!string.Equals(_currentUser.Role, "deliveryman", StringComparison.OrdinalIgnoreCase))
            throw new BusinessException("Solo los domiciliarios pueden consultar este recurso");

        var user = await _userRepository.GetByIdAsync(_currentUser.Id, cancellationToken);
        if (user == null || user.Role != UserRole.Deliveryman || !user.Active)
            throw new BusinessException("Usuario no válido");

        var branchId = _currentUser.BranchId;
        if (branchId <= 0)
            throw new BusinessException("Sucursal no asignada");

        DateOnly day;
        if (!string.IsNullOrWhiteSpace(request.Date))
        {
            if (!DateOnly.TryParse(request.Date, out day))
                throw new BusinessException("Fecha inválida (use YYYY-MM-DD)");
        }
        else
        {
            day = DateOnly.FromDateTime(ColombiaTimeHelper.GetNowInColombia().Date);
        }

        var state = await _db.DeliverymanDayStates
            .AsNoTracking()
            .FirstOrDefaultAsync(
                s => s.BranchId == branchId
                     && s.DeliverymanId == _currentUser.Id
                     && s.Date == day,
                cancellationToken);

        return new MyDeliverymanDayStateDto
        {
            DayBlocked = state?.Blocked ?? false
        };
    }
}
