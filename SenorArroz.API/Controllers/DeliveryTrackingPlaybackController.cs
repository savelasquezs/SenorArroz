using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Enums;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Authorize(Roles = "Superadmin, Admin")]
[Route("api/delivery-tracking/playback")]
public class DeliveryTrackingPlaybackController : ControllerBase
{
    private const int MaxPoints = 20_000;
    private static readonly TimeSpan MaxRange = TimeSpan.FromHours(24);
    private readonly IApplicationDbContext _db;
    private readonly IBranchContext _branchContext;
    private readonly IClock _clock;

    public DeliveryTrackingPlaybackController(
        IApplicationDbContext db,
        IBranchContext branchContext,
        IClock clock)
    {
        _db = db;
        _branchContext = branchContext;
        _clock = clock;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<DeliveryTrackingPlaybackDto>>> Get(
        [FromQuery] int[] deliverymanIds,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int? branchId = null,
        CancellationToken cancellationToken = default)
    {
        var ids = deliverymanIds.Where(x => x > 0).Distinct().ToArray();
        if (ids.Length == 0)
            return BadRequest(ApiResponse.Error("Debes seleccionar al menos un domiciliario."));
        if (!from.HasValue || !to.HasValue)
            return BadRequest(ApiResponse.Error("La fecha y hora inicial y final son obligatorias."));

        var fromUtc = ColombiaTimeHelper.EnsureUtc(from.Value);
        var toUtc = ColombiaTimeHelper.EnsureUtc(to.Value);
        if (fromUtc >= toUtc)
            return BadRequest(ApiResponse.Error("La fecha inicial debe ser anterior a la fecha final."));
        if (toUtc - fromUtc > MaxRange)
            return BadRequest(ApiResponse.Error("El rango máximo permitido es de 24 horas."));

        var resolvedBranchId = _branchContext.RequireBranch(branchId);
        var deliverymen = await _db.Users.AsNoTracking()
            .Where(x => ids.Contains(x.Id)
                && x.Role == UserRole.Deliveryman
                && x.BranchId == resolvedBranchId)
            .Select(x => new DeliverymanHeader(x.Id, x.Name, x.BranchId))
            .ToListAsync(cancellationToken);
        if (deliverymen.Count != ids.Length)
            return Forbid();

        var branchName = await _db.Branches.AsNoTracking()
            .Where(x => x.Id == resolvedBranchId)
            .Select(x => x.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? $"Sucursal #{resolvedBranchId}";

        var locationQuery = _db.DeliverymanLocations.AsNoTracking()
            .Where(x => ids.Contains(x.DeliverymanId)
                && x.RecordedAt >= fromUtc
                && x.RecordedAt <= toUtc);
        if (await locationQuery.CountAsync(cancellationToken) > MaxPoints)
            return BadRequest(ApiResponse.Error(
                $"La consulta supera el máximo de {MaxPoints:N0} puntos. Reduce el rango o la cantidad de domiciliarios."));

        var points = await locationQuery
            .OrderBy(x => x.DeliverymanId).ThenBy(x => x.RecordedAt).ThenBy(x => x.Id)
            .Select(x => new DeliveryTrackingPlaybackPointDto(
                x.Id, x.DeliverymanId, x.Latitude, x.Longitude, x.RecordedAt, x.SyncedAt,
                x.AccuracyMeters, x.HeadingDegrees, x.BatteryLevelPercent, x.InternetAvailable,
                x.GpsEnabled, x.TrackingMode, x.DeliveryRouteId, x.WorkSessionId))
            .ToListAsync(cancellationToken);
        var events = await _db.DeliveryDeviceEvents.AsNoTracking()
            .Where(x => ids.Contains(x.DeliverymanId)
                && x.RecordedAt >= fromUtc
                && x.RecordedAt <= toUtc)
            .OrderBy(x => x.DeliverymanId).ThenBy(x => x.RecordedAt).ThenBy(x => x.Id)
            .Select(x => new DeliveryTrackingPlaybackEventDto(
                x.Id, x.DeliverymanId, x.EventType, x.RecordedAt, x.SyncedAt,
                x.BatteryLevelPercent, x.InternetAvailable, x.GpsEnabled,
                x.LocationPermissionGranted, x.Details, x.WorkSessionId))
            .ToListAsync(cancellationToken);

        var stayRows = await _db.DeliveryStays.AsNoTracking()
            .Where(x => ids.Contains(x.DeliverymanId)
                && x.StartedAt <= toUtc
                && (x.EndedAt >= fromUtc || x.WorkSession.Status == DeliveryWorkSessionStatus.Active))
            .OrderBy(x => x.DeliverymanId).ThenBy(x => x.StartedAt).ThenBy(x => x.Id)
            .Select(x => new PlaybackStayRow(
                x.Id,
                x.DeliverymanId,
                x.WorkSessionId,
                x.DeliveryRouteId,
                x.NearestOrderId,
                x.FirstLocationId,
                x.LastLocationId,
                x.StartedAt,
                x.EndedAt,
                x.DurationSeconds,
                x.CenterLatitude,
                x.CenterLongitude,
                x.RadiusMeters,
                x.AverageAccuracyMeters,
                x.DistanceToBranchMeters,
                x.DistanceToNearestOrderMeters,
                x.PointCount,
                x.Classification))
            .ToListAsync(cancellationToken);

        var sessionIds = stayRows.Select(x => x.WorkSessionId).Distinct().ToList();
        var activeSessionIds = sessionIds.Count == 0
            ? []
            : await _db.DeliveryWorkSessions.AsNoTracking()
                .Where(x => sessionIds.Contains(x.Id) && x.Status == DeliveryWorkSessionStatus.Active)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);
        var latestLocations = sessionIds.Count == 0
            ? []
            : await _db.DeliverymanLocations.AsNoTracking()
                .Where(x => x.WorkSessionId.HasValue && sessionIds.Contains(x.WorkSessionId.Value))
                .Select(x => new LatestLocationRow(x.Id, x.WorkSessionId!.Value, x.RecordedAt))
                .ToListAsync(cancellationToken);
        var latestLocationBySession = latestLocations
            .GroupBy(x => x.WorkSessionId)
            .ToDictionary(
                x => x.Key,
                x => x.OrderByDescending(point => point.RecordedAt).ThenByDescending(point => point.Id).First().Id);
        var activeSessionSet = activeSessionIds.ToHashSet();

        var routeIds = stayRows.Where(x => x.DeliveryRouteId.HasValue)
            .Select(x => x.DeliveryRouteId!.Value).Distinct().ToList();
        var routeOrders = routeIds.Count == 0
            ? []
            : await _db.DeliveryRouteStops.AsNoTracking()
                .Where(x => routeIds.Contains(x.DeliveryRouteId))
                .Select(x => new PlaybackOrderRow(
                    x.DeliveryRouteId,
                    x.OrderId,
                    x.AddressSnapshotText ?? (x.Order.Address == null ? null : x.Order.Address.AddressText),
                    x.Order.Address == null ? null : x.Order.Address.Latitude,
                    x.Order.Address == null ? null : x.Order.Address.Longitude,
                    x.Order.StatusTimes,
                    null))
                .ToListAsync(cancellationToken);
        var ordersByRoute = routeOrders
            .Select(x => x with { DeliveredAt = DeliveredAt(x.StatusTimes) })
            .GroupBy(x => x.DeliveryRouteId)
            .ToDictionary(x => x.Key, x => x.ToList());
        var nowUtc = ColombiaTimeHelper.EnsureUtc(_clock.UtcNow);
        var stays = stayRows.Where(stay =>
                stay.EndedAt >= fromUtc
                || (activeSessionSet.Contains(stay.WorkSessionId)
                    && latestLocationBySession.GetValueOrDefault(stay.WorkSessionId) == stay.LastLocationId))
            .Select(stay =>
        {
            var isActive = activeSessionSet.Contains(stay.WorkSessionId)
                && latestLocationBySession.GetValueOrDefault(stay.WorkSessionId) == stay.LastLocationId;
            var contextOrders = BuildContextOrders(stay, ordersByRoute);
            return (stay.DeliverymanId, Stay: new DeliveryTrackingPlaybackStayDto(
                stay.Id,
                stay.WorkSessionId,
                stay.DeliveryRouteId,
                stay.StartedAt,
                isActive ? null : stay.EndedAt,
                isActive,
                isActive
                    ? Math.Max(0, checked((int)Math.Min(int.MaxValue, (nowUtc - stay.StartedAt).TotalSeconds)))
                    : stay.DurationSeconds,
                stay.CenterLatitude,
                stay.CenterLongitude,
                stay.RadiusMeters,
                stay.AverageAccuracyMeters,
                stay.PointCount,
                stay.FirstLocationId,
                stay.LastLocationId,
                stay.DistanceToBranchMeters,
                stay.DistanceToNearestOrderMeters,
                stay.Classification,
                contextOrders));
        }).ToList();

        var result = new DeliveryTrackingPlaybackDto(
            fromUtc,
            toUtc,
            "America/Bogota",
            deliverymen.OrderBy(x => x.Name).Select(deliveryman =>
                new DeliveryTrackingPlaybackDeliverymanDto(
                    deliveryman.Id,
                    deliveryman.Name,
                    deliveryman.BranchId,
                    branchName,
                    points.Where(x => x.DeliverymanId == deliveryman.Id).ToList(),
                    events.Where(x => x.DeliverymanId == deliveryman.Id).ToList(),
                    stays.Where(x => x.DeliverymanId == deliveryman.Id).Select(x => x.Stay).ToList()))
                .ToList());
        return Ok(ApiResponse<DeliveryTrackingPlaybackDto>.SuccessResponse(result));
    }

    private sealed record DeliverymanHeader(int Id, string Name, int BranchId);

    private static IReadOnlyList<DeliveryTrackingPlaybackOrderDto> BuildContextOrders(
        PlaybackStayRow stay,
        IReadOnlyDictionary<int, List<PlaybackOrderRow>> ordersByRoute)
    {
        if (!stay.DeliveryRouteId.HasValue
            || !ordersByRoute.TryGetValue(stay.DeliveryRouteId.Value, out var routeOrders))
            return [];

        var selected = new Dictionary<int, (PlaybackOrderRow Order, HashSet<string> Roles)>();
        void Add(PlaybackOrderRow? order, string role)
        {
            if (order is null)
                return;
            if (!selected.TryGetValue(order.OrderId, out var value))
                value = (order, []);
            value.Roles.Add(role);
            selected[order.OrderId] = value;
        }

        Add(routeOrders.FirstOrDefault(x => x.OrderId == stay.NearestOrderId), "related");
        Add(routeOrders.Where(x => x.DeliveredAt.HasValue && x.DeliveredAt <= stay.StartedAt)
            .OrderByDescending(x => x.DeliveredAt).FirstOrDefault(), "previous");
        Add(routeOrders.Where(x => x.DeliveredAt.HasValue && x.DeliveredAt >= stay.EndedAt)
            .OrderBy(x => x.DeliveredAt).FirstOrDefault(), "next");

        return selected.Values.Select(x => new DeliveryTrackingPlaybackOrderDto(
            x.Order.OrderId,
            x.Order.DeliveredAt,
            x.Order.Address,
            x.Order.Latitude,
            x.Order.Longitude,
            x.Roles.OrderBy(RoleOrder).ToList()))
            .OrderBy(x => x.DeliveredAt ?? DateTime.MaxValue)
            .ThenBy(x => x.OrderId)
            .ToList();
    }

    private static int RoleOrder(string role) => role switch
    {
        "previous" => 0,
        "related" => 1,
        "next" => 2,
        _ => 3,
    };

    private static DateTime? DeliveredAt(string statusTimes)
    {
        try
        {
            var values = JsonSerializer.Deserialize<Dictionary<string, DateTime>>(statusTimes);
            return values is not null && values.TryGetValue("delivered", out var deliveredAt)
                ? ColombiaTimeHelper.EnsureUtc(deliveredAt)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record LatestLocationRow(long Id, int WorkSessionId, DateTime RecordedAt);
    private sealed record PlaybackStayRow(
        long Id,
        int DeliverymanId,
        int WorkSessionId,
        int? DeliveryRouteId,
        int? NearestOrderId,
        long FirstLocationId,
        long LastLocationId,
        DateTime StartedAt,
        DateTime EndedAt,
        int DurationSeconds,
        decimal CenterLatitude,
        decimal CenterLongitude,
        double RadiusMeters,
        double AverageAccuracyMeters,
        double? DistanceToBranchMeters,
        double? DistanceToNearestOrderMeters,
        int PointCount,
        DeliveryStayClassification Classification);
    private sealed record PlaybackOrderRow(
        int DeliveryRouteId,
        int OrderId,
        string? Address,
        decimal? Latitude,
        decimal? Longitude,
        string StatusTimes,
        DateTime? DeliveredAt = null);
}

public record DeliveryTrackingPlaybackDto(
    DateTime From,
    DateTime To,
    string ServerTimezone,
    IReadOnlyList<DeliveryTrackingPlaybackDeliverymanDto> Deliverymen);

public record DeliveryTrackingPlaybackDeliverymanDto(
    int DeliverymanId,
    string DeliverymanName,
    int BranchId,
    string BranchName,
    IReadOnlyList<DeliveryTrackingPlaybackPointDto> Points,
    IReadOnlyList<DeliveryTrackingPlaybackEventDto> Events,
    IReadOnlyList<DeliveryTrackingPlaybackStayDto> Stays);

public record DeliveryTrackingPlaybackStayDto(
    long Id,
    int WorkSessionId,
    int? DeliveryRouteId,
    DateTime StartedAt,
    DateTime? EndedAt,
    bool IsActive,
    int DurationSeconds,
    decimal CenterLatitude,
    decimal CenterLongitude,
    double RadiusMeters,
    double AverageAccuracyMeters,
    int PointCount,
    long FirstLocationId,
    long LastLocationId,
    double? DistanceToBranchMeters,
    double? DistanceToOrderMeters,
    DeliveryStayClassification Classification,
    IReadOnlyList<DeliveryTrackingPlaybackOrderDto> Orders);

public record DeliveryTrackingPlaybackOrderDto(
    int OrderId,
    DateTime? DeliveredAt,
    string? Address,
    decimal? Latitude,
    decimal? Longitude,
    IReadOnlyList<string> Roles);

public record DeliveryTrackingPlaybackPointDto(
    long Id,
    int DeliverymanId,
    decimal Latitude,
    decimal Longitude,
    DateTime RecordedAt,
    DateTime? SyncedAt,
    double? AccuracyMeters,
    double? HeadingDegrees,
    int? BatteryLevelPercent,
    bool? InternetAvailable,
    bool? GpsEnabled,
    DeliveryTrackingMode? TrackingMode,
    int? DeliveryRouteId,
    int? WorkSessionId);

public record DeliveryTrackingPlaybackEventDto(
    long Id,
    int DeliverymanId,
    DeliveryDeviceEventType EventType,
    DateTime RecordedAt,
    DateTime SyncedAt,
    int? BatteryLevelPercent,
    bool? InternetAvailable,
    bool? GpsEnabled,
    bool? LocationPermissionGranted,
    string? Details,
    int WorkSessionId);
