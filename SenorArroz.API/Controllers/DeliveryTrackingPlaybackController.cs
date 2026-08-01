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

    public DeliveryTrackingPlaybackController(IApplicationDbContext db, IBranchContext branchContext)
    {
        _db = db;
        _branchContext = branchContext;
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
                    events.Where(x => x.DeliverymanId == deliveryman.Id).ToList()))
                .ToList());
        return Ok(ApiResponse<DeliveryTrackingPlaybackDto>.SuccessResponse(result));
    }

    private sealed record DeliverymanHeader(int Id, string Name, int BranchId);
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
    IReadOnlyList<DeliveryTrackingPlaybackEventDto> Events);

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
