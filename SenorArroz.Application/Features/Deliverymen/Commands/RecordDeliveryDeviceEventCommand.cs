using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Application.Features.Deliverymen.Commands;

public class RecordDeliveryDeviceEventCommand : IRequest
{
    public int WorkSessionId { get; set; }
    public Guid? ClientEventId { get; set; }
    public DeliveryDeviceEventType EventType { get; set; }
    public int? BatteryLevelPercent { get; set; }
    public bool? InternetAvailable { get; set; }
    public bool? GpsEnabled { get; set; }
    public bool? LocationPermissionGranted { get; set; }
    public string? Details { get; set; }
    public DateTime RecordedAt { get; set; }
}

public class RecordDeliveryDeviceEventHandler : IRequestHandler<RecordDeliveryDeviceEventCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public RecordDeliveryDeviceEventHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task Handle(RecordDeliveryDeviceEventCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
            throw new UnauthorizedAccessException("Usuario no autenticado.");
        if (request.BatteryLevelPercent is < 0 or > 100)
            throw new BusinessException("El nivel de batería debe estar entre 0 y 100.");

        var deliverymanId = _currentUser.Id;
        if (request.ClientEventId.HasValue)
        {
            var existingOwner = await _db.DeliveryDeviceEvents.AsNoTracking()
                .Where(x => x.ClientEventId == request.ClientEventId)
                .Select(x => (int?)x.DeliverymanId)
                .FirstOrDefaultAsync(cancellationToken);
            if (existingOwner == deliverymanId)
                return;
            if (existingOwner.HasValue)
                throw new BusinessException("El identificador del evento ya pertenece a otro domiciliario.");
        }

        var workSession = await _db.DeliveryWorkSessions
            .FirstOrDefaultAsync(x => x.Id == request.WorkSessionId
                                      && x.DeliverymanId == deliverymanId
                                      && x.BranchId == _currentUser.BranchId,
                cancellationToken);
        if (workSession is null)
            throw new BusinessException("La jornada laboral indicada no pertenece al domiciliario.");
        if (!string.IsNullOrWhiteSpace(_currentUser.DeviceInstallationId)
            && !string.Equals(
                _currentUser.DeviceInstallationId,
                workSession.DeviceInstallationId,
                StringComparison.Ordinal))
        {
            throw new SessionReplacedException();
        }

        var nowUtc = ColombiaTimeHelper.EnsureUtc(_clock.UtcNow);
        _db.DeliveryDeviceEvents.Add(new DeliveryDeviceEvent
        {
            DeliverymanId = deliverymanId,
            WorkSessionId = request.WorkSessionId,
            ClientEventId = request.ClientEventId ?? Guid.NewGuid(),
            EventType = request.EventType,
            BatteryLevelPercent = request.BatteryLevelPercent,
            InternetAvailable = request.InternetAvailable,
            GpsEnabled = request.GpsEnabled,
            LocationPermissionGranted = request.LocationPermissionGranted,
            Details = NormalizeDetails(request.Details),
            RecordedAt = ColombiaTimeHelper.EnsureUtc(request.RecordedAt),
            SyncedAt = nowUtc,
        });
        workSession.LastCommunicationAt = nowUtc;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static string? NormalizeDetails(string? details)
    {
        var normalized = string.IsNullOrWhiteSpace(details) ? null : details.Trim();
        return normalized is { Length: > 500 } ? normalized[..500] : normalized;
    }
}
