using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.Deliverymen.Commands;

public class CloseMyDeliveryWorkSessionCommand : IRequest;

public class CloseMyDeliveryWorkSessionHandler : IRequestHandler<CloseMyDeliveryWorkSessionCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IBackgroundWorkSignal<DeliveryWorkSessionScheduleWork>? _scheduleSignal;

    public CloseMyDeliveryWorkSessionHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IClock clock,
        IBackgroundWorkSignal<DeliveryWorkSessionScheduleWork>? scheduleSignal = null)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _scheduleSignal = scheduleSignal;
    }

    public async Task Handle(CloseMyDeliveryWorkSessionCommand request, CancellationToken cancellationToken)
    {
        var query = _db.DeliveryWorkSessions.Where(
            x => x.DeliverymanId == _currentUser.Id
                 && x.Status == DeliveryWorkSessionStatus.Active);
        if (!string.IsNullOrWhiteSpace(_currentUser.DeviceInstallationId))
        {
            query = query.Where(
                x => x.DeviceInstallationId == _currentUser.DeviceInstallationId);
        }

        var session = await query.FirstOrDefaultAsync(cancellationToken);
        if (session is null) return;

        session.Close(
            ColombiaTimeHelper.EnsureUtc(_clock.UtcNow),
            DeliveryWorkSessionEndReason.UserChange);
        await _db.SaveChangesAsync(cancellationToken);
        _scheduleSignal?.Pulse();
    }
}
