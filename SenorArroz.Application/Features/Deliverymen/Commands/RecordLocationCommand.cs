using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Options;

namespace SenorArroz.Application.Features.Deliverymen.Commands;

public sealed record RecordLocationResult(bool ContinueActiveTracking, int? DeliveryRouteId,
    DeliveryReturnReview? ReturnReview = null);

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
    private readonly IDeliveryAutoCompletionService? _autoCompletion;
    private readonly ILogger<RecordLocationHandler>? _logger;
    private readonly IDeliveryAutoCompletionRouteLock? _routeLock;
    private readonly RoutingMeasurementOptions _measurement;

    public RecordLocationHandler(IApplicationDbContext db, ICurrentUser currentUser,
        IOrderNotificationService notifications, IClock clock,
        IDeliveryAutoCompletionService? autoCompletion = null,
        ILogger<RecordLocationHandler>? logger = null,
        IDeliveryAutoCompletionRouteLock? routeLock = null, IOptions<DeliveryRoutingOptions>? routingOptions = null)
    {
        _db = db;
        _currentUser = currentUser;
        _notifications = notifications;
        _clock = clock;
        _autoCompletion = autoCompletion;
        _logger = logger;
        _routeLock = routeLock;
        _measurement = routingOptions?.Value.Measurement ?? new RoutingMeasurementOptions();
    }

    public async Task<RecordLocationResult> Handle(RecordLocationCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) throw new UnauthorizedAccessException("Usuario no autenticado.");
        var deliverymanId = _currentUser.Id;
        var branchId = _currentUser.BranchId;
        var now = ColombiaTimeHelper.EnsureUtc(_clock.UtcNow);
        var recorded = ColombiaTimeHelper.EnsureUtc(request.RecordedAt);
        if (request.ClientPointId.HasValue)
        {
            var owner = await _db.DeliverymanLocations.AsNoTracking()
                .Where(x => x.ClientPointId == request.ClientPointId)
                .Select(x => (int?)x.DeliverymanId).FirstOrDefaultAsync(ct);
            if (owner == deliverymanId) return await CurrentTrackingResultAsync(deliverymanId, ct);
            if (owner.HasValue) throw new BusinessException("El identificador de la ubicación ya pertenece a otro domiciliario.");
        }
        Validate(request);
        var session = await _db.DeliveryWorkSessions.FirstOrDefaultAsync(x =>
            x.DeliverymanId == deliverymanId && x.BranchId == branchId && x.Id == request.WorkSessionId, ct);
        if (session is null) throw new BusinessException("La jornada laboral del dispositivo ya no está activa.");
        if (!string.IsNullOrWhiteSpace(_currentUser.DeviceInstallationId)
            && !string.Equals(_currentUser.DeviceInstallationId, session.DeviceInstallationId, StringComparison.Ordinal))
            throw new SessionReplacedException();
        var deadline = session.EndedAt.HasValue && session.EndedAt < session.AutoCloseAt
            ? session.EndedAt.Value : session.AutoCloseAt;
        if (recorded < session.StartedAt || recorded >= deadline)
            throw new BusinessException("La ubicación fue capturada fuera de la jornada laboral.");
        if (session.Status == DeliveryWorkSessionStatus.Active && now >= session.AutoCloseAt)
        {
            session.Close(now, DeliveryWorkSessionEndReason.AutomaticClosure);
            _db.DeliveryDeviceEvents.Add(DeliveryDeviceEvent.ForClosure(session, now, DeliveryWorkSessionEndReason.AutomaticClosure));
        }
        var isLatest = !await _db.DeliverymanLocations.AsNoTracking()
            .AnyAsync(x => x.DeliverymanId == deliverymanId && x.RecordedAt > recorded, ct);
        var live = isLatest && recorded >= now.AddMinutes(-2) && recorded <= now.AddSeconds(15)
            && session.Status == DeliveryWorkSessionStatus.Active && session.AutoCloseAt > now;
        int? routeId = request.DeliveryRouteId;
        if (routeId.HasValue && !await _db.DeliveryRoutes.AsNoTracking().AnyAsync(r =>
            r.Id == routeId && r.DeliverymanId == deliverymanId && r.BranchId == branchId, ct))
            throw new BusinessException("La ruta indicada no pertenece al domiciliario.");
        if (live)
        {
            // The client may still carry a previous trip ID. The server owns the live association.
            routeId = await DeliveryRouteScope.Current(_db, branchId, deliverymanId, now)
                .AsNoTracking().Select(r => (int?)r.Id).FirstOrDefaultAsync(ct);
        }
        else if (routeId.HasValue)
        {
            var capturedRoute = await _db.DeliveryRoutes.AsNoTracking().SingleAsync(r => r.Id == routeId, ct);
            if (!DeliveryRouteScope.ContainsCapture(capturedRoute, session, recorded))
                routeId = null;
        }
        // Historical points keep their original association; never attach them to a new trip.
        var location = new DeliverymanLocation
        {
            DeliverymanId = deliverymanId, WorkSessionId = session.Id, DeliveryRouteId = routeId,
            ClientPointId = request.ClientPointId ?? Guid.NewGuid(), Latitude = request.Latitude,
            Longitude = request.Longitude, AccuracyMeters = request.AccuracyMeters,
            HeadingDegrees = request.HeadingDegrees, BatteryLevelPercent = request.BatteryLevelPercent,
            InternetAvailable = request.InternetAvailable, GpsEnabled = request.GpsEnabled ?? true,
            TrackingMode = request.TrackingMode ?? (routeId.HasValue ? DeliveryTrackingMode.ActiveDelivery : DeliveryTrackingMode.Light),
            RecordedAt = recorded, SyncedAt = now
        };
        _db.DeliverymanLocations.Add(location);
        session.LastCommunicationAt = now;
        await _db.SaveChangesAsync(ct);
        DeliveryReturnReview? review = null;
        if (live && routeId.HasValue)
        {
            try
            {
                if (_routeLock is null) await DeliveryRouteScope.ObserveDepartureAsync(_db, location, session, now, ct, _logger, _measurement);
                else await _routeLock.ExecuteAsync(routeId.Value,
                    token => DeliveryRouteScope.ObserveDepartureAsync(_db, location, session, now, token, _logger, _measurement), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { _logger?.LogWarning(ex, "Departure evidence failed route={RouteId}", routeId); }
            if (_autoCompletion is not null)
            {
                try { await _autoCompletion.EvaluateLocationAsync(location, ct); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                { _logger?.LogWarning(ex, "AutoDelivery evaluation failed location={LocationId} route={RouteId}", location.Id, routeId); }
                try { review = await _autoCompletion.ReconcileReturnAsync(location, ct); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                { _logger?.LogWarning(ex, "AutoDelivery return reconciliation failed location={LocationId} route={RouteId}", location.Id, routeId); }
            }
        }
        try
        {
            await _notifications.NotifyDeliverymanLocation(branchId, deliverymanId, routeId,
                (double)request.Latitude, (double)request.Longitude, recorded);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { _logger?.LogWarning(ex, "Location {LocationId} recorded, but realtime notification failed", location.Id); }
        return (await CurrentTrackingResultAsync(deliverymanId, ct)) with { ReturnReview = review };
    }

    private async Task<RecordLocationResult> CurrentTrackingResultAsync(int deliverymanId, CancellationToken ct)
    {
        var routeId = await DeliveryRouteScope.Current(_db, _currentUser.BranchId, deliverymanId,
                ColombiaTimeHelper.EnsureUtc(_clock.UtcNow)).AsNoTracking()
            .Select(r => (int?)r.Id).FirstOrDefaultAsync(ct);
        return new RecordLocationResult(routeId.HasValue, routeId);
    }

    private static void Validate(RecordLocationCommand request)
    {
        if (request.Latitude is < -90 or > 90 || request.Longitude is < -180 or > 180)
            throw new BusinessException("Las coordenadas de la ubicación no son válidas.");
        if (request.AccuracyMeters is double accuracy && (accuracy < 0 || !double.IsFinite(accuracy)))
            throw new BusinessException("La precisión GPS no es válida.");
        if (request.HeadingDegrees is double heading && (!double.IsFinite(heading) || heading < 0 || heading > 360))
            throw new BusinessException("La dirección de movimiento debe estar entre 0 y 360 grados.");
        if (request.BatteryLevelPercent is < 0 or > 100)
            throw new BusinessException("El nivel de batería debe estar entre 0 y 100.");
        if (request.TrackingMode == DeliveryTrackingMode.Stopped)
            throw new BusinessException("No se pueden registrar ubicaciones en modo detenido.");
    }
}
