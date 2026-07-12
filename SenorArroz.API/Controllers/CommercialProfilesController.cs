using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Route("api/commercial-profiles")]
[Authorize(Roles = "Superadmin, Admin")]
public class CommercialProfilesController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IFirebaseGcsStorage _storage;

    public CommercialProfilesController(ApplicationDbContext db, ICurrentUser currentUser, IFirebaseGcsStorage storage)
    { _db = db; _currentUser = currentUser; _storage = storage; }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<CommercialProfileDto>>>> Get([FromQuery] int branchId, CancellationToken ct)
    {
        if (!CanAccess(branchId)) return Forbid();
        var rows = await _db.CommercialProfiles.AsNoTracking().Where(x => x.BranchId == branchId)
            .OrderBy(x => x.Name).Select(x => ToDto(x)).ToListAsync(ct);
        return Ok(ApiResponse<List<CommercialProfileDto>>.SuccessResponse(rows));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<CommercialProfileDto>>> Create([FromBody] SaveCommercialProfileDto dto, CancellationToken ct)
    {
        if (!CanAccess(dto.BranchId)) return Forbid();
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest(ApiResponse<CommercialProfileDto>.ErrorResponse("El nombre es requerido."));
        var row = new CommercialProfile { BranchId = dto.BranchId, Name = dto.Name.Trim(), Description = Clean(dto.Description), Ingredients = Clean(dto.Ingredients) };
        _db.CommercialProfiles.Add(row); await _db.SaveChangesAsync(ct);
        return Ok(ApiResponse<CommercialProfileDto>.SuccessResponse(ToDto(row)));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ApiResponse<CommercialProfileDto>>> Update(int id, [FromBody] SaveCommercialProfileDto dto, CancellationToken ct)
    {
        var row = await _db.CommercialProfiles.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return NotFound(ApiResponse<CommercialProfileDto>.ErrorResponse("Ficha no encontrada."));
        if (!CanAccess(row.BranchId) || row.BranchId != dto.BranchId) return Forbid();
        row.Name = dto.Name.Trim(); row.Description = Clean(dto.Description); row.Ingredients = Clean(dto.Ingredients);
        await _db.SaveChangesAsync(ct);
        return Ok(ApiResponse<CommercialProfileDto>.SuccessResponse(ToDto(row)));
    }

    [HttpPost("{id:int}/photo")]
    [RequestSizeLimit(8_000_000)]
    public async Task<ActionResult<ApiResponse<CommercialProfileDto>>> UploadPhoto(int id, IFormFile file, CancellationToken ct)
    {
        var row = await _db.CommercialProfiles.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return NotFound(ApiResponse<CommercialProfileDto>.ErrorResponse("Ficha no encontrada."));
        if (!CanAccess(row.BranchId)) return Forbid();
        if (file.Length == 0 || !file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return BadRequest(ApiResponse<CommercialProfileDto>.ErrorResponse("Selecciona una imagen válida."));
        await using var stream = file.OpenReadStream(); using var ms = new MemoryStream(); await stream.CopyToAsync(ms, ct);
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        row.PhotoUrl = await _storage.UploadPublicObjectAsync(ms.ToArray(), $"commercial-profiles/{row.BranchId}/{row.Id}/{Guid.NewGuid():N}{ext}", file.ContentType, ct);
        await _db.SaveChangesAsync(ct);
        return Ok(ApiResponse<CommercialProfileDto>.SuccessResponse(ToDto(row)));
    }

    [HttpDelete("{id:int}/photo")]
    public async Task<IActionResult> DeletePhoto(int id, CancellationToken ct)
    {
        var row = await _db.CommercialProfiles.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return NotFound(); if (!CanAccess(row.BranchId)) return Forbid();
        await _storage.DeleteObjectsWithPrefixAsync($"commercial-profiles/{row.BranchId}/{row.Id}/", ct);
        row.PhotoUrl = null; await _db.SaveChangesAsync(ct); return NoContent();
    }

    private bool CanAccess(int branchId) => _currentUser.Role.Equals("superadmin", StringComparison.OrdinalIgnoreCase) || _currentUser.BranchId == branchId;
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static CommercialProfileDto ToDto(CommercialProfile x) => new(x.Id, x.BranchId, x.Name, x.Description, x.Ingredients, x.PhotoUrl);
}

public record CommercialProfileDto(int Id, int BranchId, string Name, string? Description, string? Ingredients, string? PhotoUrl);
public record SaveCommercialProfileDto(int BranchId, string Name, string? Description, string? Ingredients);
