using System.ComponentModel.DataAnnotations;
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
[Route("api/delivery-tracking-incidents")]
public class DeliveryTrackingIncidentsController : ControllerBase
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public DeliveryTrackingIncidentsController(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<DeliveryTrackingIncidentListItemDto>>>> GetAll(
        [FromQuery] int? branchId = null,
        [FromQuery] int? deliverymanId = null,
        [FromQuery] string? reviewStatus = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolveBranch(branchId, out var resolvedBranchId))
            return Forbid();
        if (!TryParseReviewStatus(reviewStatus, out var parsedReviewStatus))
            return BadRequest(ApiResponse.Error("Estado de revisión inválido."));

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = _db.DeliveryTrackingIncidents.AsNoTracking().AsQueryable();
        if (resolvedBranchId.HasValue)
            query = query.Where(x => x.BranchId == resolvedBranchId.Value);
        if (deliverymanId.HasValue)
            query = query.Where(x => x.DeliverymanId == deliverymanId.Value);
        if (parsedReviewStatus.HasValue)
            query = query.Where(x => x.ReviewStatus == parsedReviewStatus.Value);
        if (from.HasValue)
            query = query.Where(x => x.StartedAt >= ColombiaTimeHelper.EnsureUtc(from.Value));
        if (to.HasValue)
            query = query.Where(x => x.StartedAt < ColombiaTimeHelper.EnsureUtc(to.Value));

        var totalCount = await query.CountAsync(cancellationToken);
        var incidents = await query
            .OrderByDescending(x => x.StartedAt)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var names = await LoadNamesAsync(incidents.Select(x => x.DeliverymanId), cancellationToken);
        var branches = await LoadBranchNamesAsync(incidents.Select(x => x.BranchId), cancellationToken);
        var incidentIds = incidents.Select(x => x.Id).ToList();
        var evidenceStates = await _db.DeliveryIncidentLocationEvidence.AsNoTracking()
            .Where(x => incidentIds.Contains(x.IncidentId))
            .Select(x => new { x.IncidentId, x.Id, x.RecordedAt, x.GpsEnabled, x.InternetAvailable })
            .ToListAsync(cancellationToken);
        var lastStates = evidenceStates.GroupBy(x => x.IncidentId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(x => x.RecordedAt).ThenByDescending(x => x.Id)
                    .Select(x => new IncidentLastState(x.IncidentId, x.GpsEnabled, x.InternetAvailable))
                    .First());

        var result = new PagedResult<DeliveryTrackingIncidentListItemDto>
        {
            Items = incidents.Select(incident =>
            {
                lastStates.TryGetValue(incident.Id, out var state);
                return new DeliveryTrackingIncidentListItemDto(
                    incident.Id,
                    incident.BranchId,
                    branches.GetValueOrDefault(incident.BranchId, $"Sucursal #{incident.BranchId}"),
                    incident.DeliverymanId,
                    names.GetValueOrDefault(incident.DeliverymanId, $"Domiciliario #{incident.DeliverymanId}"),
                    incident.WorkSessionId,
                    incident.StartedAt,
                    incident.EndedAt,
                    incident.DurationSeconds,
                    incident.StayClassification,
                    incident.FinalClassification,
                    incident.ReviewStatus,
                    incident.OrderId,
                    incident.OrderAddressSnapshot,
                    incident.DistanceToOrderMeters,
                    incident.AverageAccuracyMeters,
                    state?.GpsEnabled,
                    state?.InternetAvailable,
                    incident.EvidenceComplete);
            }).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize),
        };
        return Ok(ApiResponse<PagedResult<DeliveryTrackingIncidentListItemDto>>.SuccessResponse(result));
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<DeliveryTrackingIncidentDetailDto>>> GetById(
        long id,
        CancellationToken cancellationToken)
    {
        var incident = await _db.DeliveryTrackingIncidents.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (incident is null)
            return NotFound(ApiResponse.Error("Incidente no encontrado."));
        if (!CanAccessBranch(incident.BranchId))
            return Forbid();

        var locations = await _db.DeliveryIncidentLocationEvidence.AsNoTracking()
            .Where(x => x.IncidentId == id)
            .OrderBy(x => x.RecordedAt)
            .ThenBy(x => x.Id)
            .Select(x => new DeliveryIncidentLocationEvidenceDto(
                x.SourceLocationId,
                x.IsCorePoint,
                x.Latitude,
                x.Longitude,
                x.AccuracyMeters,
                x.HeadingDegrees,
                x.BatteryLevelPercent,
                x.InternetAvailable,
                x.GpsEnabled,
                x.TrackingMode,
                x.RecordedAt,
                x.SyncedAt))
            .ToListAsync(cancellationToken);
        var events = await _db.DeliveryIncidentDeviceEventEvidence.AsNoTracking()
            .Where(x => x.IncidentId == id)
            .OrderBy(x => x.RecordedAt)
            .ThenBy(x => x.Id)
            .Select(x => new DeliveryIncidentDeviceEventEvidenceDto(
                x.SourceDeviceEventId,
                x.EventType,
                x.BatteryLevelPercent,
                x.InternetAvailable,
                x.GpsEnabled,
                x.LocationPermissionGranted,
                x.Details,
                x.RecordedAt,
                x.SyncedAt))
            .ToListAsync(cancellationToken);
        var deliverymanName = await _db.Users.AsNoTracking()
            .Where(x => x.Id == incident.DeliverymanId).Select(x => x.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? $"Domiciliario #{incident.DeliverymanId}";
        var branchName = await _db.Branches.AsNoTracking()
            .Where(x => x.Id == incident.BranchId).Select(x => x.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? $"Sucursal #{incident.BranchId}";
        var reviewerName = incident.ReviewedByUserId.HasValue
            ? await _db.Users.AsNoTracking().Where(x => x.Id == incident.ReviewedByUserId.Value)
                .Select(x => x.Name).FirstOrDefaultAsync(cancellationToken)
            : null;

        var dto = new DeliveryTrackingIncidentDetailDto(
            incident.Id,
            incident.IncidentType,
            incident.BranchId,
            branchName,
            incident.DeliverymanId,
            deliverymanName,
            incident.WorkSessionId,
            incident.DeliveryRouteId,
            incident.OrderId,
            incident.StayClassification,
            incident.ClassificationReason,
            incident.FinalClassification,
            incident.ReviewStatus,
            incident.StartedAt,
            incident.EndedAt,
            incident.DurationSeconds,
            incident.CenterLatitude,
            incident.CenterLongitude,
            incident.RadiusMeters,
            incident.AverageAccuracyMeters,
            incident.DistanceToBranchMeters,
            incident.DistanceToOrderMeters,
            incident.OrderAddressSnapshot,
            incident.OrderLatitudeSnapshot,
            incident.OrderLongitudeSnapshot,
            incident.OrderStatusSnapshot,
            incident.AdminNotes,
            incident.DeliverymanExplanation,
            incident.ReviewedByUserId,
            reviewerName,
            incident.ReviewedAt,
            incident.EvidenceComplete,
            locations,
            events);
        return Ok(ApiResponse<DeliveryTrackingIncidentDetailDto>.SuccessResponse(dto));
    }

    [HttpPut("{id:long}/review")]
    public async Task<ActionResult<ApiResponse<DeliveryTrackingIncidentDetailDto>>> Review(
        long id,
        [FromBody] ReviewDeliveryTrackingIncidentRequest request,
        CancellationToken cancellationToken)
    {
        var incident = await _db.DeliveryTrackingIncidents
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (incident is null)
            return NotFound(ApiResponse.Error("Incidente no encontrado."));
        if (!CanAccessBranch(incident.BranchId))
            return Forbid();

        incident.ReviewStatus = request.ReviewStatus;
        incident.FinalClassification = request.FinalClassification;
        incident.AdminNotes = Clean(request.AdminNotes);
        incident.DeliverymanExplanation = Clean(request.DeliverymanExplanation);
        incident.ReviewedByUserId = _currentUser.Id;
        incident.ReviewedAt = ColombiaTimeHelper.EnsureUtc(_clock.UtcNow);
        incident.UpdatedAt = incident.ReviewedAt.Value;
        await _db.SaveChangesAsync(cancellationToken);

        return await GetById(id, cancellationToken);
    }

    private bool TryResolveBranch(int? requestedBranchId, out int? branchId)
    {
        if (Roles.IsSuperadmin(_currentUser.Role))
        {
            branchId = requestedBranchId;
            return true;
        }
        branchId = _currentUser.BranchId;
        return Roles.IsAdmin(_currentUser.Role)
            && (!requestedBranchId.HasValue || requestedBranchId.Value == branchId.Value);
    }

    private bool CanAccessBranch(int branchId) =>
        Roles.IsSuperadmin(_currentUser.Role)
        || (Roles.IsAdmin(_currentUser.Role) && _currentUser.BranchId == branchId);

    private static bool TryParseReviewStatus(
        string? value,
        out DeliveryIncidentReviewStatus? status)
    {
        status = value?.Trim().ToLowerInvariant() switch
        {
            null or "" => null,
            "pending" => DeliveryIncidentReviewStatus.Pending,
            "justified" => DeliveryIncidentReviewStatus.Justified,
            "not_justified" => DeliveryIncidentReviewStatus.NotJustified,
            "gps_error" => DeliveryIncidentReviewStatus.GpsError,
            "technical_failure" => DeliveryIncidentReviewStatus.TechnicalFailure,
            "closed_without_action" => DeliveryIncidentReviewStatus.ClosedWithoutAction,
            "referred_to_disciplinary_process" => DeliveryIncidentReviewStatus.ReferredToDisciplinaryProcess,
            _ => null,
        };
        return string.IsNullOrWhiteSpace(value) || status.HasValue;
    }

    private async Task<Dictionary<int, string>> LoadNamesAsync(
        IEnumerable<int> ids,
        CancellationToken cancellationToken)
    {
        var values = ids.Distinct().ToList();
        return await _db.Users.AsNoTracking().Where(x => values.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
    }

    private async Task<Dictionary<int, string>> LoadBranchNamesAsync(
        IEnumerable<int> ids,
        CancellationToken cancellationToken)
    {
        var values = ids.Distinct().ToList();
        return await _db.Branches.AsNoTracking().Where(x => values.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record IncidentLastState(long IncidentId, bool? GpsEnabled, bool? InternetAvailable);
}

public record DeliveryTrackingIncidentListItemDto(
    long Id,
    int BranchId,
    string BranchName,
    int DeliverymanId,
    string DeliverymanName,
    int WorkSessionId,
    DateTime StartedAt,
    DateTime EndedAt,
    int DurationSeconds,
    DeliveryStayClassification? AutomaticClassification,
    DeliveryStayClassification? FinalClassification,
    DeliveryIncidentReviewStatus ReviewStatus,
    int? OrderId,
    string? OrderAddress,
    double? DistanceToOrderMeters,
    double AverageAccuracyMeters,
    bool? GpsEnabled,
    bool? InternetAvailable,
    bool EvidenceComplete);

public record DeliveryTrackingIncidentDetailDto(
    long Id,
    DeliveryTrackingIncidentType IncidentType,
    int BranchId,
    string BranchName,
    int DeliverymanId,
    string DeliverymanName,
    int WorkSessionId,
    int? DeliveryRouteId,
    int? OrderId,
    DeliveryStayClassification? AutomaticClassification,
    string? ClassificationReason,
    DeliveryStayClassification? FinalClassification,
    DeliveryIncidentReviewStatus ReviewStatus,
    DateTime StartedAt,
    DateTime EndedAt,
    int DurationSeconds,
    decimal CenterLatitude,
    decimal CenterLongitude,
    double RadiusMeters,
    double AverageAccuracyMeters,
    double? DistanceToBranchMeters,
    double? DistanceToOrderMeters,
    string? OrderAddress,
    decimal? OrderLatitude,
    decimal? OrderLongitude,
    string? OrderStatus,
    string? AdminNotes,
    string? DeliverymanExplanation,
    int? ReviewedByUserId,
    string? ReviewedByUserName,
    DateTime? ReviewedAt,
    bool EvidenceComplete,
    IReadOnlyList<DeliveryIncidentLocationEvidenceDto> Locations,
    IReadOnlyList<DeliveryIncidentDeviceEventEvidenceDto> DeviceEvents);

public record DeliveryIncidentLocationEvidenceDto(
    long SourceLocationId,
    bool IsCorePoint,
    decimal Latitude,
    decimal Longitude,
    double? AccuracyMeters,
    double? HeadingDegrees,
    int? BatteryLevelPercent,
    bool? InternetAvailable,
    bool? GpsEnabled,
    DeliveryTrackingMode? TrackingMode,
    DateTime RecordedAt,
    DateTime? SyncedAt);

public record DeliveryIncidentDeviceEventEvidenceDto(
    long SourceDeviceEventId,
    DeliveryDeviceEventType EventType,
    int? BatteryLevelPercent,
    bool? InternetAvailable,
    bool? GpsEnabled,
    bool? LocationPermissionGranted,
    string? Details,
    DateTime RecordedAt,
    DateTime SyncedAt);

public class ReviewDeliveryTrackingIncidentRequest
{
    public DeliveryIncidentReviewStatus ReviewStatus { get; set; }
    public DeliveryStayClassification? FinalClassification { get; set; }

    [StringLength(2000)]
    public string? AdminNotes { get; set; }

    [StringLength(2000)]
    public string? DeliverymanExplanation { get; set; }
}
