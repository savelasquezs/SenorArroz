using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Shared.Models;
using SenorArroz.Domain.Enums;

namespace SenorArroz.API.Controllers;

[ApiController]
public class BranchMenuController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IFirebaseGcsStorage _storage;
    private readonly ICurrentTenant _currentTenant;
    private readonly ITenantUsageMeter _usage;
    public BranchMenuController(ApplicationDbContext db, ICurrentUser currentUser, IFirebaseGcsStorage storage, ICurrentTenant currentTenant, ITenantUsageMeter usage)
    { _db = db; _currentUser = currentUser; _storage = storage; _currentTenant = currentTenant; _usage = usage; }

    [HttpGet("api/branches/{branchId:int}/menu")]
    [Authorize(Roles = "Superadmin, Admin")]
    public async Task<ActionResult<ApiResponse<BranchMenuDto>>> GetConfig(int branchId, CancellationToken ct)
    {
        if (!CanAccess(branchId)) return Forbid();
        var menu = await _db.Branches.AsNoTracking().Where(x => x.Id == branchId)
            .Select(x => new BranchMenuDto(x.Id, x.Name, x.MenuImageUrl1, x.MenuImageUrl2)).FirstOrDefaultAsync(ct);
        return menu is null ? NotFound() : Ok(ApiResponse<BranchMenuDto>.SuccessResponse(menu));
    }

    [HttpPost("api/branches/{branchId:int}/menu/images/{slot:int}")]
    [Authorize(Roles = "Superadmin, Admin")]
    [RequestSizeLimit(10_000_000)]
    public async Task<ActionResult<ApiResponse<BranchMenuDto>>> Upload(int branchId, int slot, IFormFile file, CancellationToken ct)
    {
        if (!CanAccess(branchId)) return Forbid();
        if (slot is < 1 or > 2) return BadRequest(ApiResponse<BranchMenuDto>.ErrorResponse("La posición debe ser 1 o 2."));
        if (file.Length == 0 || !file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return BadRequest(ApiResponse<BranchMenuDto>.ErrorResponse("Selecciona una imagen válida."));
        var branch = await _db.Branches.FirstOrDefaultAsync(x => x.Id == branchId, ct); if (branch is null) return NotFound();
        await using var input = file.OpenReadStream(); using var ms = new MemoryStream(); await input.CopyToAsync(ms, ct);
        var prefix = TenantPrefix($"branch-menu/{branchId}/slot-{slot}");
        await _storage.DeleteObjectsWithPrefixAsync($"{prefix}/", ct);
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var content = ms.ToArray();
        var url = await _storage.UploadPublicObjectAsync(content, $"{prefix}/{Guid.NewGuid():N}{ext}", file.ContentType, ct);
        await _usage.AddStorageBytesAsync(content.LongLength, ct);
        if (slot == 1) branch.MenuImageUrl1 = url; else branch.MenuImageUrl2 = url;
        await _db.SaveChangesAsync(ct);
        return Ok(ApiResponse<BranchMenuDto>.SuccessResponse(new(branch.Id, branch.Name, branch.MenuImageUrl1, branch.MenuImageUrl2)));
    }

    [HttpDelete("api/branches/{branchId:int}/menu/images/{slot:int}")]
    [Authorize(Roles = "Superadmin, Admin")]
    public async Task<IActionResult> Delete(int branchId, int slot, CancellationToken ct)
    {
        if (!CanAccess(branchId)) return Forbid(); if (slot is < 1 or > 2) return BadRequest();
        var branch = await _db.Branches.FirstOrDefaultAsync(x => x.Id == branchId, ct); if (branch is null) return NotFound();
        await _storage.DeleteObjectsWithPrefixAsync($"{TenantPrefix($"branch-menu/{branchId}/slot-{slot}")}/", ct);
        if (slot == 1) branch.MenuImageUrl1 = null; else branch.MenuImageUrl2 = null;
        await _db.SaveChangesAsync(ct); return NoContent();
    }

    [HttpGet("api/public/menu")]
    [AllowAnonymous]
    [Produces("text/html")]
    public async Task<ContentResult> PublicMenu([FromQuery] int? branchId, CancellationToken ct)
    {
        var query = _db.Branches.AsNoTracking().Where(x => x.Tenant.Status == TenantStatus.Active && (x.MenuImageUrl1 != null || x.MenuImageUrl2 != null));
        if (branchId.HasValue) query = query.Where(x => x.Id == branchId.Value);
        var menu = await query.OrderBy(x => x.Id).Select(x => new BranchMenuDto(x.Id, x.Name, x.MenuImageUrl1, x.MenuImageUrl2)).FirstOrDefaultAsync(ct);
        if (menu is null) return Content("<!doctype html><html><meta name=viewport content='width=device-width'><body><p>La carta aún no está disponible.</p></body></html>", "text/html");
        var images = new[] { menu.ImageUrl1, menu.ImageUrl2 }.Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => $"<img src=\"{WebUtility.HtmlEncode(x)}\" alt=\"Carta\" loading=\"lazy\">");
        var html = $"<!doctype html><html lang=\"es\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>Carta - {WebUtility.HtmlEncode(menu.BranchName)}</title><style>body{{margin:0;background:#f3f4f6;font-family:system-ui}}main{{max-width:900px;margin:auto;padding:12px}}h1{{font-size:1.25rem;text-align:center}}img{{display:block;width:100%;height:auto;margin:0 0 12px;border-radius:12px;box-shadow:0 2px 12px #0002}}</style></head><body><main><h1>{WebUtility.HtmlEncode(menu.BranchName)}</h1>{string.Join("", images)}</main></body></html>";
        return Content(html, "text/html");
    }

    private bool CanAccess(int branchId) => _currentUser.Role.Equals("superadmin", StringComparison.OrdinalIgnoreCase) || _currentUser.BranchId == branchId;
    private string TenantPrefix(string path) => $"tenants/{(_currentTenant.TenantPublicId ?? throw new InvalidOperationException("No existe un tenant autenticado.")):D}/{path}";
}

public record BranchMenuDto(int BranchId, string BranchName, string? ImageUrl1, string? ImageUrl2);
