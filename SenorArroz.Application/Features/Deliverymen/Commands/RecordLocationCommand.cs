using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Application.Features.Deliverymen.Commands;

public sealed record RecordLocationResult(bool ContinueActiveTracking, int? DeliveryRouteId);

public class RecordLocationCommand : IRequest<RecordLocationResult>
{
    public int WorkSessionId { get; set; }
    public Guid? ClientPointId { get; set; }
    public int? DeliveryRouteId { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public double? AccuracyMeters { get; set; }
    public double? HeadingDegrees { get; set; }
    public int? BatteryLevelPercent { get; set; }
    public bool? InternetAvailable { get; set; }
    public bool? GpsEnabled { get; set; }
    public DeliveryTrackingMode? TrackingMode { get; set; }
    public DateTime RecordedAt { get; set; }
}

public class RecordLocationHandler : IRequestHandler<RecordLocationCommand, RecordLocationResult>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IOrderNotificationService _notifications;
    private readonly IClock _clock;

    public RecordLocationHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IOrderNotificationService notifications,
        IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _notifications = notifications;
        _clock = clock;
    }

    public async Task<RecordLocationResult> Handle(RecordLocationCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
            throw new UnauthorizedAccessException("Usuario no autenticado.");

        var deliverymanId = _currentUser.Id;
        var branchId = _currentUser.BranchId;
        var nowUtc = ColombiaTimeHelper.EnsureUtc(_clock.UtcNow);
        var recordedAtUtc = ColombiaTimeHelper.EnsureUtc(request.RecordedAt);

        if (request.ClientPointId.HasValue)
        {
            var existingOwner = await _db.DeliverymanLocations.AsNoTracking()
                .Where(x => x.ClientPointId == request.ClientPointId)
                .Select(x => (int?)x.DeliverymanId)
                .FirstOrDefaultAsync(cancellationToken);
            if (existingOwner == deliverymanId)
                return await CurrentTrackingResultAsync(deliverymanId, cancellationToken);
            if (existingOwner.HasValue)
                throw new BusinessException("El identificador de la ubicación ya pertenece a otro domiciliario.");
        }

        Validate(request);

        var workSession = await _db.DeliveryWorkSessions
            .FirstOrDefaultAsync(x => x.DeliverymanId == deliverymanId
                                      && x.BranchId == branchId
                                      && x.Id == request.WorkSessionId,
                cancellationToken);
        if (workSession is null)
            throw new BusinessException("La jornada laboral del dispositivo ya no está activa.");

        var captureDeadline = workSession.EndedAt.HasValue && workSession.EndedAt.Value < workSession.AutoCloseAt
            ? workSession.EndedAt.Value
            : workSession.AutoCloseAt;
        if (recordedAtUtc < workSession.StartedAt || recordedAtUtc >= captureDeadline)
            throw new BusinessException("La ubicación fue capturada fuera de la jornada laboral.");

        if (workSession.Status == DeliveryWorkSessionStatus.Active && nowUtc >= workSession.AutoCloseAt)
        {
            workSession.Close(nowUtc, DeliveryWorkSessionEndReason.AutomaticClosure);
            _db.DeliveryDeviceEvents.Add(DeliveryDeviceEvent.ForClosure(
                workSession,
                nowUtc,
                DeliveryWorkSessionEndReason.AutomaticClosure));
        }

        int? routeId = request.DeliveryRouteId;
        if (routeId.HasValue)
        {
            var routeBelongsToDeliveryman = await _db.DeliveryRoutes.AsNoTracking()
                .AnyAsync(x => x.Id == routeId.Value
                               && x.DeliverymanId == deliverymanId
                               && x.BranchId == branchId,
                    cancellationToken);
            if (!routeBelongsToDeliveryman)
                throw new BusinessException("La ruta indicada no pertenece al domiciliario.");
        }
        else
        {
            routeId = await FindCurrentRouteIdAsync(deliverymanId, cancellationToken);
        }

        if (routeId.HasValue)
            await RepairStaleRouteClockAsync(routeId.Value, recordedAtUtc, cancellationToken);

        _db.DeliverymanLocations.Add(new DeliverymanLocation
        {
            DeliverymanId = deliverymanId,
            WorkSessionId = workSession.Id,
            DeliveryRouteId = routeId,
            ClientPointId = request.ClientPointId ?? Guid.NewGuid(),
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            AccuracyMeters = request.AccuracyMeters,
            HeadingDegrees = request.HeadingDegrees,
            BatteryLevelPercent = request.BatteryLevelPercent,
            InternetAvailable = request.InternetAvailable ?? true,
            GpsEnabled = request.GpsEnabled ?? true,
            TrackingMode = request.TrackingMode
                           ?? (routeId.HasValue
                               ? DeliveryTrackingMode.ActiveDelivery
                               : DeliveryTrackingMode.Light),
            RecordedAt = recordedAtUtc,
            SyncedAt = nowUtc,
        });
        workSession.LastCommunicationAt = nowUtc;
        await _db.SaveChangesAsync(cancellationToken);

        await _notifications.NotifyDeliverymanLocation(
            branchId,
            deliverymanId,
            routeId,
            (double)request.Latitude,
            (double)request.Longitude,
            recordedAtUtc);

        if (routeId.HasValue)
        {
            await TryCompleteRouteAtBranchAsync(
                routeId.Value,
                deliverymanId,
                branchId,
                request.Latitude,
                request.Longitude,
                recordedAtUtc,
                cancellationToken);
        }

        return await CurrentTrackingResultAsync(deliverymanId, cancellationToken);
    }

    private async Task TryCompleteRouteAtBranchAsync(
        int routeId,
        int deliverymanId,
        int branchId,
        decimal latitude,
        decimal longitude,
        DateTime recordedAtUtc,
        CancellationToken cancellationToken)
    {
        var route = await _db.DeliveryRoutes
            .Include(r => r.Stops)
            .FirstOrDefaultAsync(r => r.Id == routeId
                                      && r.DeliverymanId == deliverymanId
                                      && r.BranchId == branchId
                                      && r.Status == DeliveryRouteStatus.InProgress,
                cancellationToken);
        if (route is null || route.Stops.Count == 0)
            return;

        var orderIds = route.Stops.Select(s => s.OrderId).ToList();
        var allTerminal = await _db.Orders
            .Where(o => orderIds.Contains(o.Id))
            .AllAsync(o => o.Status == OrderStatus.Delivered || o.Status == OrderStatus.Cancelled,
                cancellationToken);
        if (!allTerminal)
            return;

        var branch = await _db.Branches.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == branchId, cancellationToken);
        if (branch?.Latitude is null || branch.Longitude is null)
            return;

        var distance = GeoHelper.HaversineDistanceMeters(
            (double)latitude,
            (double)longitude,
            (double)branch.Latitude.Value,
            (double)branch.Longitude.Value);
        if (distance > Math.Max(1, branch.DeliveryTrackingAllowedDistanceMeters))
            return;

        route.CompletedAtUtc = recordedAtUtc;
        if (route.RouteStartedAtUtc.HasValue)
        {
            route.ActualDurationSeconds = (int)Math.Max(
                0,
                (recordedAtUtc - route.RouteStartedAtUtc.Value).TotalSeconds);
            route.MetSla = route.MetaDurationSeconds.HasValue
                ? route.ActualDurationSeconds <= route.MetaDurationSeconds.Value
                : null;
        }
        route.Status = DeliveryRouteStatus.Completed;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task RepairStaleRouteClockAsync(
        int routeId,
        DateTime recordedAtUtc,
        CancellationToken cancellationToken)
    {
        var todayStartUtc = ColombiaTimeHelper.GetTodayStartInUtcFromUtc(recordedAtUtc);
        var route = await _db.DeliveryRoutes
            .Include(r => r.Stops)
            .FirstOrDefaultAsync(r => r.Id == routeId
                                      && r.Status == DeliveryRouteStatus.InProgress
                                      && r.RouteStartedAtUtc < todayStartUtc,
                cancellationToken);
        if (route is null)
            return;

        var orderIds = route.Stops.Select(s => s.OrderId).ToList();
        var activeOrders = await _db.Orders
            .Where(o => orderIds.Contains(o.Id)
                        && o.Status != OrderStatus.Delivered
                        && o.Status != OrderStatus.Cancelled)
            .ToListAsync(cancellationToken);
        if (activeOrders.Count == 0)
            return;

        var operationalStart = activeOrders
            .Select(o =>
            {
                var times = o.GetStatusTimes();
                if (times.TryGetValue("ontheway", out var onTheWay)) return (DateTime?)onTheWay;
                if (times.TryGetValue(Order.DeliveryManAssignedStatusTimeKey, out var assigned)) return assigned;
                return null;
            })
            .Where(x => x.HasValue && x.Value >= todayStartUtc)
            .Select(x => x!.Value)
            .DefaultIfEmpty(recordedAtUtc)
            .Min();

        route.RouteStartedAtUtc = ColombiaTimeHelper.EnsureUtc(operationalStart);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<RecordLocationResult> CurrentTrackingResultAsync(
        int deliverymanId,
        CancellationToken cancellationToken)
    {
        var routeId = await _db.DeliveryRoutes.AsNoTracking()
            .Where(r => r.DeliverymanId == deliverymanId
                        && (r.Status == DeliveryRouteStatus.Open
                            || r.Status == DeliveryRouteStatus.InProgress))
            .OrderByDescending(r => r.Status == DeliveryRouteStatus.InProgress)
            .ThenByDescending(r => r.LastAssignmentAtUtc)
            .Select(r => (int?)r.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return new RecordLocationResult(routeId.HasValue, routeId);
    }

    private async Task<int?> FindCurrentRouteIdAsync(int deliverymanId, CancellationToken cancellationToken)
    {
        var activeRoute = await _db.DeliveryRoutes
            .Where(r => r.DeliverymanId == deliverymanId
                        && (r.Status == DeliveryRouteStatus.Open
                            || r.Status == DeliveryRouteStatus.InProgress))
            .OrderByDescending(r => r.LastAssignmentAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (activeRoute is null)
            return null;

        return activeRoute.Id;
    }

    private static void Validate(RecordLocationCommand request)
    {
        if (request.Latitude is < -90 or > 90 || request.Longitude is < -180 or > 180)
            throw new BusinessException("Las coordenadas de la ubicación no son válidas.");
        if (request.AccuracyMeters is < 0)
            throw new BusinessException("La precisión GPS no puede ser negativa.");
        if (request.HeadingDegrees is < 0 or > 360)
            throw new BusinessException("La dirección de movimiento debe estar entre 0 y 360 grados.");
        if (request.BatteryLevelPercent is < 0 or > 100)
            throw new BusinessException("El nivel de batería debe estar entre 0 y 100.");
        if (request.TrackingMode == DeliveryTrackingMode.Stopped)
            throw new BusinessException("No se pueden registrar ubicaciones en modo detenido.");
    }
}
