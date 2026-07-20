using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Authorize(Roles = "Superadmin, Admin")]
[Route("api/branches/{branchId:int}/delivery-authorized-places")]
public class DeliveryAuthorizedPlacesController : ControllerBase
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public DeliveryAuthorizedPlacesController(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<DeliveryAuthorizedPlaceDto>>>> Get(
        int branchId,
        CancellationToken cancellationToken)
    {
        if (!CanAccess(branchId))
            return Forbid();
        if (!await _db.Branches.AnyAsync(x => x.Id == branchId, cancellationToken))
            return NotFound(ApiResponse.Error("Sucursal no encontrada."));
        var result = await _db.DeliveryAuthorizedPlaces.AsNoTracking()
            .Where(x => x.BranchId == branchId)
            .OrderByDescending(x => x.Active)
            .ThenBy(x => x.Name)
            .Select(x => new DeliveryAuthorizedPlaceDto(
                x.Id,
                x.BranchId,
                x.Name,
                x.Latitude,
                x.Longitude,
                x.RadiusMeters,
                x.Active,
                x.CreatedAt,
                x.UpdatedAt))
            .ToListAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<DeliveryAuthorizedPlaceDto>>.SuccessResponse(result));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<DeliveryAuthorizedPlaceDto>>> Create(
        int branchId,
        [FromBody] SaveDeliveryAuthorizedPlaceRequest request,
        CancellationToken cancellationToken)
    {
        if (!CanAccess(branchId))
            return Forbid();
        if (!await _db.Branches.AnyAsync(x => x.Id == branchId, cancellationToken))
            return NotFound(ApiResponse.Error("Sucursal no encontrada."));
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(ApiResponse.Error("El nombre del lugar es obligatorio."));

        var nowUtc = ColombiaTimeHelper.EnsureUtc(_clock.UtcNow);
        var place = new DeliveryAuthorizedPlace
        {
            BranchId = branchId,
            Name = request.Name.Trim(),
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            RadiusMeters = request.RadiusMeters,
            Active = request.Active,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        };
        _db.DeliveryAuthorizedPlaces.Add(place);
        await InvalidateStaysAsync(branchId, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return StatusCode(
            StatusCodes.Status201Created,
            ApiResponse<DeliveryAuthorizedPlaceDto>.SuccessResponse(ToDto(place), "Lugar autorizado creado."));
    }

    [HttpPut("{placeId:int}")]
    public async Task<ActionResult<ApiResponse<DeliveryAuthorizedPlaceDto>>> Update(
        int branchId,
        int placeId,
        [FromBody] SaveDeliveryAuthorizedPlaceRequest request,
        CancellationToken cancellationToken)
    {
        if (!CanAccess(branchId))
            return Forbid();
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(ApiResponse.Error("El nombre del lugar es obligatorio."));
        var place = await _db.DeliveryAuthorizedPlaces
            .FirstOrDefaultAsync(x => x.Id == placeId && x.BranchId == branchId, cancellationToken);
        if (place is null)
            return NotFound(ApiResponse.Error("Lugar autorizado no encontrado."));

        place.Name = request.Name.Trim();
        place.Latitude = request.Latitude;
        place.Longitude = request.Longitude;
        place.RadiusMeters = request.RadiusMeters;
        place.Active = request.Active;
        place.UpdatedAt = ColombiaTimeHelper.EnsureUtc(_clock.UtcNow);
        await InvalidateStaysAsync(branchId, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(ApiResponse<DeliveryAuthorizedPlaceDto>.SuccessResponse(ToDto(place), "Lugar autorizado actualizado."));
    }

    [HttpDelete("{placeId:int}")]
    public async Task<ActionResult<ApiResponse>> Disable(
        int branchId,
        int placeId,
        CancellationToken cancellationToken)
    {
        if (!CanAccess(branchId))
            return Forbid();
        var place = await _db.DeliveryAuthorizedPlaces
            .FirstOrDefaultAsync(x => x.Id == placeId && x.BranchId == branchId, cancellationToken);
        if (place is null)
            return NotFound(ApiResponse.Error("Lugar autorizado no encontrado."));

        place.Active = false;
        place.UpdatedAt = ColombiaTimeHelper.EnsureUtc(_clock.UtcNow);
        await InvalidateStaysAsync(branchId, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(ApiResponse.Success("Lugar autorizado desactivado."));
    }

    private async Task InvalidateStaysAsync(int branchId, CancellationToken cancellationToken)
    {
        var stays = await (
            from stay in _db.DeliveryStays
            join session in _db.DeliveryWorkSessions on stay.WorkSessionId equals session.Id
            where session.BranchId == branchId
            select stay)
            .ToListAsync(cancellationToken);
        foreach (var stay in stays)
            stay.InvalidateClassification();
    }

    private bool CanAccess(int branchId) =>
        Roles.IsSuperadmin(_currentUser.Role)
        || (Roles.IsAdmin(_currentUser.Role) && _currentUser.BranchId == branchId);

    private static DeliveryAuthorizedPlaceDto ToDto(DeliveryAuthorizedPlace place) => new(
        place.Id,
        place.BranchId,
        place.Name,
        place.Latitude,
        place.Longitude,
        place.RadiusMeters,
        place.Active,
        place.CreatedAt,
        place.UpdatedAt);
}

public record DeliveryAuthorizedPlaceDto(
    int Id,
    int BranchId,
    string Name,
    decimal Latitude,
    decimal Longitude,
    int RadiusMeters,
    bool Active,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public class SaveDeliveryAuthorizedPlaceRequest
{
    [Required, StringLength(100, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [Range(-90, 90)]
    public decimal Latitude { get; set; }

    [Range(-180, 180)]
    public decimal Longitude { get; set; }

    [Range(1, 5000)]
    public int RadiusMeters { get; set; } = 50;

    public bool Active { get; set; } = true;
}
