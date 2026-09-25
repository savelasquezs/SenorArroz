using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Route("api/whatsapp/agent-tools")]
[Authorize(Roles = "Superadmin, Admin, Cashier")]
public class WhatsAppAgentToolsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IInventoryService _inventory;
    private readonly IBranchContext _branchContext;
    public WhatsAppAgentToolsController(ApplicationDbContext db, IInventoryService inventory, IBranchContext branchContext)
    {
        _db = db;
        _inventory = inventory;
        _branchContext = branchContext;
    }

    [HttpGet("products")]
    public async Task<ActionResult<ApiResponse<List<WhatsAppProductCatalogDto>>>> SearchProducts([FromQuery] int branchId, [FromQuery] string? search, [FromQuery] int? servesPeople, CancellationToken ct)
    {
        _branchContext.EnsureAccess(branchId);
        var query = _db.Products.AsNoTracking().Include(x => x.Category).Include(x => x.CommercialProfile)
            .Where(x => x.Category.BranchId == branchId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(x => EF.Functions.ILike(x.Name, pattern) || (x.CommercialProfile != null &&
                (EF.Functions.ILike(x.CommercialProfile.Name, pattern) || (x.CommercialProfile.Description != null && EF.Functions.ILike(x.CommercialProfile.Description, pattern)) || (x.CommercialProfile.Ingredients != null && EF.Functions.ILike(x.CommercialProfile.Ingredients, pattern)))));
        }
        if (servesPeople.HasValue) query = query.Where(x => x.ServesPeopleMin <= servesPeople && x.ServesPeopleMax >= servesPeople);
        var products = await query.OrderBy(x => x.Name).Take(50).ToListAsync(ct);
        var availability = (await _inventory.GetAvailabilityAsync(products.Select(x => x.Id).ToArray(), branchId, ct)).ToDictionary(x => x.ProductId);
        var rows = products.Select(x => new WhatsAppProductCatalogDto(x.Id, x.Name,
            x.CommercialProfile?.Name, x.CommercialProfile?.Description, x.CommercialProfile?.Ingredients, x.CommercialProfile?.PhotoUrl,
            x.ServesPeopleMin, x.ServesPeopleMax, x.Price, availability.TryGetValue(x.Id, out var value) && value.Available, branchId)).ToList();
        return Ok(ApiResponse<List<WhatsAppProductCatalogDto>>.SuccessResponse(rows));
    }

    [HttpGet("products/{productId:int}")]
    public async Task<ActionResult<ApiResponse<WhatsAppProductCatalogDto>>> ProductDetails(int productId, CancellationToken ct)
    {
        var x = await _db.Products.AsNoTracking().Include(p => p.Category).Include(p => p.CommercialProfile).FirstOrDefaultAsync(p => p.Id == productId, ct);
        if (x is null) return NotFound();
        _branchContext.EnsureAccess(x.Category.BranchId);
        var availability = (await _inventory.GetAvailabilityAsync([x.Id], x.Category.BranchId, ct)).SingleOrDefault();
        var dto = new WhatsAppProductCatalogDto(x.Id, x.Name, x.CommercialProfile?.Name, x.CommercialProfile?.Description, x.CommercialProfile?.Ingredients, x.CommercialProfile?.PhotoUrl, x.ServesPeopleMin, x.ServesPeopleMax, x.Price, availability?.Available == true, x.Category.BranchId);
        return Ok(ApiResponse<WhatsAppProductCatalogDto>.SuccessResponse(dto));
    }
}

public record WhatsAppProductCatalogDto(int ProductId, string ProductName, string? CommercialProfileName, string? Description,
    string? Ingredients, string? PhotoUrl, int? ServesPeopleMin, int? ServesPeopleMax, int Price, bool Availability, int BranchId);
