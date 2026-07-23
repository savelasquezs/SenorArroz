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
[Route("api/delivery-tracking-alerts")]
public class DeliveryTrackingAlertsController : ControllerBase
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public DeliveryTrackingAlertsController(IApplicationDbContext db, ICurrentUser currentUser, IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<DeliveryTrackingAlertDto>>>> GetAll(
        [FromQuery] int? branchId = null,
        [FromQuery] string? status = "active",
        [FromQuery] string? severity = null,
        [FromQuery] string? alertType = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolveBranch(branchId, out var resolvedBranchId))
            return Forbid();
        if (!TryParse<DeliveryTrackingAlertStatus>(status, out var parsedStatus)
            || !TryParse<DeliveryTrackingAlertSeverity>(severity, out var parsedSeverity)
            || !TryParse<DeliveryTrackingAlertType>(alertType, out var parsedType))
            return BadRequest(ApiResponse.Error("Uno de los filtros de alertas no es válido."));

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = _db.DeliveryTrackingAlerts.AsNoTracking().AsQueryable();
        if (resolvedBranchId.HasValue)
            query = query.Where(x => x.BranchId == resolvedBranchId.Value);
        if (parsedStatus.HasValue)
            query = query.Where(x => x.Status == parsedStatus.Value);
        if (parsedSeverity.HasValue)
            query = query.Where(x => x.Severity == parsedSeverity.Value);
        if (parsedType.HasValue)
            query = query.Where(x => x.AlertType == parsedType.Value);
        if (from.HasValue)
            query = query.Where(x => x.OccurredAt >= ColombiaTimeHelper.EnsureUtc(from.Value));
        if (to.HasValue)
            query = query.Where(x => x.OccurredAt < ColombiaTimeHelper.EnsureUtc(to.Value));

        var totalCount = await query.CountAsync(cancellationToken);
        var alerts = await query.OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var userIds = alerts.Select(x => x.DeliverymanId)
            .Concat(alerts.Where(x => x.ResolvedByUserId.HasValue).Select(x => x.ResolvedByUserId!.Value))
            .Distinct().ToList();
        var names = await _db.Users.AsNoTracking().Where(x => userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var branchIds = alerts.Select(x => x.BranchId).Distinct().ToList();
        var branches = await _db.Branches.AsNoTracking().Where(x => branchIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var result = new PagedResult<DeliveryTrackingAlertDto>
        {
            Items = alerts.Select(x => ToDto(x, names, branches)).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize),
        };
        return Ok(ApiResponse<PagedResult<DeliveryTrackingAlertDto>>.SuccessResponse(result));
    }

    [HttpPut("{id:long}/resolve")]
    public async Task<ActionResult<ApiResponse<DeliveryTrackingAlertDto>>> Resolve(
        long id,
        [FromBody] ResolveDeliveryTrackingAlertRequest request,
        CancellationToken cancellationToken)
    {
        var alert = await _db.DeliveryTrackingAlerts.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (alert is null)
            return NotFound(ApiResponse.Error("Alerta no encontrada."));
        if (!CanAccessBranch(alert.BranchId))
            return Forbid();
        if (alert.Status == DeliveryTrackingAlertStatus.Resolved)
            return BadRequest(ApiResponse.Error("La alerta ya está resuelta."));

        var nowUtc = ColombiaTimeHelper.EnsureUtc(_clock.UtcNow);
        alert.Status = DeliveryTrackingAlertStatus.Resolved;
        alert.ResolvedAt = nowUtc;
        alert.ResolvedByUserId = _currentUser.Id;
        alert.ResolutionReason = string.IsNullOrWhiteSpace(request.Reason) ? "Cerrada desde el panel administrativo." : request.Reason.Trim();
        alert.UpdatedAt = nowUtc;
        await _db.SaveChangesAsync(cancellationToken);

        var names = await _db.Users.AsNoTracking()
            .Where(x => x.Id == alert.DeliverymanId || x.Id == _currentUser.Id)
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var branchName = await _db.Branches.AsNoTracking().Where(x => x.Id == alert.BranchId)
            .Select(x => x.Name).FirstOrDefaultAsync(cancellationToken);
        return Ok(ApiResponse<DeliveryTrackingAlertDto>.SuccessResponse(
            ToDto(alert, names, new Dictionary<int, string> { [alert.BranchId] = branchName ?? $"Sucursal #{alert.BranchId}" }),
            "Alerta resuelta."));
    }

    private bool TryResolveBranch(int? requested, out int? branchId)
    {
        if (Roles.IsSuperadmin(_currentUser.Role))
        {
            branchId = requested;
            return true;
        }
        branchId = _currentUser.BranchId;
        return Roles.IsAdmin(_currentUser.Role)
            && branchId.HasValue
            && (!requested.HasValue || requested.Value == branchId.Value);
    }

    private bool CanAccessBranch(int branchId) => Roles.IsSuperadmin(_currentUser.Role)
        || (Roles.IsAdmin(_currentUser.Role) && _currentUser.BranchId == branchId);

    private static bool TryParse<T>(string? value, out T? parsed) where T : struct, Enum
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(value))
            return true;
        var pascal = string.Concat(value.Trim().Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()));
        if (!Enum.TryParse<T>(pascal, true, out var result))
            return false;
        parsed = result;
        return true;
    }

    private static DeliveryTrackingAlertDto ToDto(
        Domain.Entities.DeliveryTrackingAlert alert,
        IReadOnlyDictionary<int, string> names,
        IReadOnlyDictionary<int, string> branches) => new(
        alert.Id,
        alert.BranchId,
        branches.GetValueOrDefault(alert.BranchId, $"Sucursal #{alert.BranchId}"),
        alert.DeliverymanId,
        names.GetValueOrDefault(alert.DeliverymanId, $"Domiciliario #{alert.DeliverymanId}"),
        alert.WorkSessionId,
        alert.IncidentId,
        alert.AlertType,
        alert.Severity,
        alert.Status,
        alert.Title,
        alert.Message,
        alert.OccurredAt,
        alert.LastOccurredAt,
        alert.RecoveredAt,
        alert.DurationSeconds,
        alert.StartLatitude,
        alert.StartLongitude,
        alert.StartLocationRecordedAt,
        alert.EndLatitude,
        alert.EndLongitude,
        alert.EndLocationRecordedAt,
        alert.OccurrenceCount,
        alert.ResolvedAt,
        alert.ResolvedByUserId,
        alert.ResolvedByUserId.HasValue ? names.GetValueOrDefault(alert.ResolvedByUserId.Value) : null,
        alert.ResolutionReason);
}

public record DeliveryTrackingAlertDto(
    long Id,
    int BranchId,
    string BranchName,
    int DeliverymanId,
    string DeliverymanName,
    int? WorkSessionId,
    long? IncidentId,
    DeliveryTrackingAlertType AlertType,
    DeliveryTrackingAlertSeverity Severity,
    DeliveryTrackingAlertStatus Status,
    string Title,
    string Message,
    DateTime OccurredAt,
    DateTime LastOccurredAt,
    DateTime? RecoveredAt,
    int? DurationSeconds,
    decimal? StartLatitude,
    decimal? StartLongitude,
    DateTime? StartLocationRecordedAt,
    decimal? EndLatitude,
    decimal? EndLongitude,
    DateTime? EndLocationRecordedAt,
    int OccurrenceCount,
    DateTime? ResolvedAt,
    int? ResolvedByUserId,
    string? ResolvedByUserName,
    string? ResolutionReason);

public class ResolveDeliveryTrackingAlertRequest
{
    [StringLength(500)]
    public string? Reason { get; set; }
}
