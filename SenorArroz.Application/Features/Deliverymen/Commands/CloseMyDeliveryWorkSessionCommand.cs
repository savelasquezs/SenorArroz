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
    private readonly SenorArroz.Application.Common.Services.DeliveryOperationsService _operations;

    public CloseMyDeliveryWorkSessionHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IClock clock,
        SenorArroz.Application.Common.Services.DeliveryOperationsService operations,
        IBackgroundWorkSignal<DeliveryWorkSessionScheduleWork>? scheduleSignal = null)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _scheduleSignal = scheduleSignal;
        _operations = operations;
    }

    public async Task Handle(CloseMyDeliveryWorkSessionCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = _db.Database.IsRelational() && _db.Database.CurrentTransaction is null
            ? await _db.Database.BeginTransactionAsync(cancellationToken) : null;
        await DeliveryRouteScope.LockDriversAsync(_db, [_currentUser.Id], cancellationToken);
        await DeliveryRouteScope.EnsureCurrentSessionAsync(_db, _currentUser, cancellationToken);
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

        await _operations.CompleteActiveAsync("logout", null, cancellationToken);

        session.Close(
            ColombiaTimeHelper.EnsureUtc(_clock.UtcNow),
            DeliveryWorkSessionEndReason.UserChange);
        await _db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        _scheduleSignal?.Pulse();
    }
}
