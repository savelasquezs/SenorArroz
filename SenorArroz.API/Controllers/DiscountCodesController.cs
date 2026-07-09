using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.DiscountCodes.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Route("api/branches/{branchId:int}/discount-codes")]
public class DiscountCodesController : ControllerBase
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public DiscountCodesController(IApplicationDbContext db, ICurrentUser currentUser, IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    [HttpGet]
    [Authorize(Roles = "Superadmin,Admin")]
    public async Task<ActionResult<ApiResponse<List<DiscountCodeDto>>>> GetAll(
        int branchId,
        CancellationToken cancellationToken)
    {
        if (!CanManageBranch(branchId))
            return Forbid();

        var now = _clock.UtcNow;
        var codes = await BaseQuery()
            .AsNoTracking()
            .Where(x => x.BranchId == branchId)
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.Code)
            .ToListAsync(cancellationToken);

        return Ok(ApiResponse<List<DiscountCodeDto>>.SuccessResponse(
            codes.Select(x => ToDto(x, now)).ToList(),
            "Codigos promocionales obtenidos."));
    }

    [HttpGet("validate")]
    [Authorize(Roles = "Superadmin,Admin,Cashier")]
    public async Task<ActionResult<ApiResponse<DiscountCodeDto>>> Validate(
        int branchId,
        [FromQuery] string code,
        [FromQuery] int? orderValue,
        CancellationToken cancellationToken)
    {
        if (!CanReadBranch(branchId))
            return Forbid();

        var normalizedCode = NormalizeCode(code);
        if (string.IsNullOrWhiteSpace(normalizedCode))
            return BadRequest(ApiResponse<DiscountCodeDto>.ErrorResponse("Ingresa un codigo promocional."));

        var item = await BaseQuery()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.BranchId == branchId && x.Code == normalizedCode, cancellationToken);

        if (item is null)
            return NotFound(ApiResponse<DiscountCodeDto>.ErrorResponse("Codigo promocional no encontrado."));

        var now = _clock.UtcNow;
        var eligibilityError = GetEligibilityError(item, now, orderValue);
        if (eligibilityError is not null)
            return BadRequest(ApiResponse<DiscountCodeDto>.ErrorResponse(eligibilityError));

        return Ok(ApiResponse<DiscountCodeDto>.SuccessResponse(ToDto(item, now), "Codigo promocional valido."));
    }

    [HttpPost]
    [Authorize(Roles = "Superadmin,Admin")]
    public Task<ActionResult<ApiResponse<DiscountCodeDto>>> Create(
        int branchId,
        [FromBody] UpsertDiscountCodeDto dto,
        CancellationToken cancellationToken)
    {
        dto.Id = null;
        return Save(branchId, dto, cancellationToken);
    }

    [HttpPut]
    [Authorize(Roles = "Superadmin,Admin")]
    public Task<ActionResult<ApiResponse<DiscountCodeDto>>> Update(
        int branchId,
        [FromBody] UpsertDiscountCodeDto dto,
        CancellationToken cancellationToken) =>
        Save(branchId, dto, cancellationToken);

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Superadmin,Admin")]
    public async Task<ActionResult<ApiResponse<DiscountCodeDto?>>> Delete(
        int branchId,
        int id,
        CancellationToken cancellationToken)
    {
        if (!CanManageBranch(branchId))
            return Forbid();

        var code = await _db.DiscountCodes.FirstOrDefaultAsync(x => x.Id == id && x.BranchId == branchId, cancellationToken);
        if (code is null)
            return NotFound(ApiResponse<DiscountCodeDto?>.ErrorResponse("Codigo promocional no encontrado."));

        _db.DiscountCodes.Remove(code);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponse<DiscountCodeDto?>.SuccessResponse(null, "Codigo promocional eliminado."));
    }

    private async Task<ActionResult<ApiResponse<DiscountCodeDto>>> Save(
        int branchId,
        UpsertDiscountCodeDto dto,
        CancellationToken cancellationToken)
    {
        if (!CanManageBranch(branchId))
            return Forbid();

        if (!await _db.Branches.AsNoTracking().AnyAsync(x => x.Id == branchId, cancellationToken))
            return NotFound(ApiResponse<DiscountCodeDto>.ErrorResponse("Sucursal no encontrada."));

        var normalizedCode = NormalizeCode(dto.Code);
        if (string.IsNullOrWhiteSpace(normalizedCode))
            return BadRequest(ApiResponse<DiscountCodeDto>.ErrorResponse("Code es requerido."));

        if (!TryParseName(dto.Type, out LoyaltyRewardType type))
            return BadRequest(ApiResponse<DiscountCodeDto>.ErrorResponse("Type debe ser GiftProduct, FreeDelivery o PercentageDiscount."));

        var validation = await ValidateBusinessRules(branchId, dto, type, cancellationToken);
        if (validation is not null)
            return BadRequest(ApiResponse<DiscountCodeDto>.ErrorResponse(validation));

        var duplicate = await _db.DiscountCodes
            .AsNoTracking()
            .AnyAsync(x => x.BranchId == branchId && x.Code == normalizedCode && x.Id != (dto.Id ?? 0), cancellationToken);
        if (duplicate)
            return BadRequest(ApiResponse<DiscountCodeDto>.ErrorResponse("Ya existe un codigo promocional igual en esta sucursal."));

        DiscountCode? entity;
        if (dto.Id.HasValue && dto.Id.Value > 0)
        {
            entity = await _db.DiscountCodes.FirstOrDefaultAsync(x => x.Id == dto.Id.Value && x.BranchId == branchId, cancellationToken);
            if (entity is null)
                return NotFound(ApiResponse<DiscountCodeDto>.ErrorResponse("Codigo promocional no encontrado."));
        }
        else
        {
            entity = new DiscountCode { BranchId = branchId };
            _db.DiscountCodes.Add(entity);
        }

        entity.Code = normalizedCode;
        entity.Type = type;
        entity.GiftProductId = type == LoyaltyRewardType.GiftProduct ? dto.GiftProductId : null;
        entity.DiscountPercentage = type == LoyaltyRewardType.PercentageDiscount ? dto.DiscountPercentage : null;
        entity.StartsAt = dto.StartsAt;
        entity.EndsAt = dto.EndsAt;
        entity.MinimumOrderValue = dto.MinimumOrderValue;
        entity.IsActive = dto.IsActive;
        entity.Label = dto.Label.Trim();
        entity.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();

        await _db.SaveChangesAsync(cancellationToken);

        var saved = await BaseQuery()
            .AsNoTracking()
            .FirstAsync(x => x.Id == entity.Id, cancellationToken);

        return Ok(ApiResponse<DiscountCodeDto>.SuccessResponse(ToDto(saved, _clock.UtcNow), "Codigo promocional guardado."));
    }

    private IQueryable<DiscountCode> BaseQuery() =>
        _db.DiscountCodes
            .Include(x => x.GiftProduct)
                .ThenInclude(p => p!.Category);

    private bool CanReadBranch(int branchId)
    {
        if (Roles.IsSuperadmin(_currentUser.Role))
            return true;
        return Roles.IsAdmin(_currentUser.Role) || Roles.IsCashier(_currentUser.Role)
            ? _currentUser.BranchId == branchId
            : false;
    }

    private bool CanManageBranch(int branchId)
    {
        if (Roles.IsSuperadmin(_currentUser.Role))
            return true;
        return Roles.IsAdmin(_currentUser.Role) && _currentUser.BranchId == branchId;
    }

    private async Task<string?> ValidateBusinessRules(
        int branchId,
        UpsertDiscountCodeDto dto,
        LoyaltyRewardType type,
        CancellationToken cancellationToken)
    {
        if (dto.StartsAt == default)
            return "StartsAt es requerido.";
        if (dto.EndsAt.HasValue && dto.EndsAt <= dto.StartsAt)
            return "EndsAt debe ser mayor que StartsAt.";
        if (dto.MinimumOrderValue is < 0)
            return "MinimumOrderValue no puede ser negativo.";
        if (string.IsNullOrWhiteSpace(dto.Label))
            return "Label es requerido.";

        if (type == LoyaltyRewardType.GiftProduct)
        {
            if (!dto.GiftProductId.HasValue)
                return "GiftProductId es requerido para Producto gratis.";
            if (dto.DiscountPercentage.HasValue)
                return "DiscountPercentage debe ser null para Producto gratis.";

            var giftProduct = await _db.Products
                .AsNoTracking()
                .Include(x => x.Category)
                .FirstOrDefaultAsync(x => x.Id == dto.GiftProductId.Value, cancellationToken);

            if (giftProduct is null)
                return "El producto regalo no existe.";
            if (!giftProduct.Active)
                return "El producto regalo debe estar activo.";
            if (giftProduct.Category.BranchId != branchId)
                return "El producto regalo no pertenece a la sucursal.";
            if (!IsGiftsCategory(giftProduct.Category.Name))
                return "El producto regalo debe pertenecer a la categoria Regalos.";

            return null;
        }

        if (type == LoyaltyRewardType.FreeDelivery)
        {
            if (dto.GiftProductId.HasValue)
                return "GiftProductId debe ser null para Domicilio gratis.";
            if (dto.DiscountPercentage.HasValue)
                return "DiscountPercentage debe ser null para Domicilio gratis.";

            return null;
        }

        if (dto.GiftProductId.HasValue)
            return "GiftProductId debe ser null para Descuento porcentual.";
        if (!dto.DiscountPercentage.HasValue)
            return "DiscountPercentage es requerido para Descuento porcentual.";
        if (dto.DiscountPercentage <= 0 || dto.DiscountPercentage > 100)
            return "DiscountPercentage debe ser mayor a 0 y maximo 100.";

        return null;
    }

    private static string? GetEligibilityError(DiscountCode code, DateTime now, int? orderValue)
    {
        if (!code.IsActive)
            return "El codigo promocional esta inactivo.";
        if (code.StartsAt > now)
            return "El codigo promocional aun no esta vigente.";
        if (code.EndsAt.HasValue && code.EndsAt <= now)
            return "El codigo promocional ya vencio.";
        if (orderValue.HasValue && code.MinimumOrderValue.HasValue && orderValue.Value < code.MinimumOrderValue.Value)
            return $"El pedido minimo para este codigo es {code.MinimumOrderValue.Value:C0}.";

        return null;
    }

    private static DiscountCodeDto ToDto(DiscountCode code, DateTime now)
    {
        return new DiscountCodeDto
        {
            Id = code.Id,
            BranchId = code.BranchId,
            Code = code.Code,
            Type = code.Type.ToString(),
            GiftProductId = code.GiftProductId,
            GiftProductName = code.GiftProduct?.Name,
            GiftProductCategoryName = code.GiftProduct?.Category?.Name,
            DiscountPercentage = code.DiscountPercentage,
            StartsAt = code.StartsAt,
            EndsAt = code.EndsAt,
            MinimumOrderValue = code.MinimumOrderValue,
            IsActive = code.IsActive,
            Label = code.Label,
            Description = code.Description,
            CreatedAt = code.CreatedAt,
            UpdatedAt = code.UpdatedAt,
            Status = GetStatus(code, now)
        };
    }

    private static string GetStatus(DiscountCode code, DateTime now)
    {
        if (!code.IsActive)
            return "inactive";
        if (code.StartsAt > now)
            return "scheduled";
        if (code.EndsAt.HasValue && code.EndsAt <= now)
            return "expired";
        return "active";
    }

    private static bool TryParseName<TEnum>(string? value, out TEnum result)
        where TEnum : struct, Enum
    {
        var normalized = NormalizeToken(value);
        foreach (var name in Enum.GetNames<TEnum>())
        {
            if (NormalizeToken(name) == normalized)
                return Enum.TryParse(name, out result);
        }

        result = default;
        return false;
    }

    private static string NormalizeCode(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToUpperInvariant();

    private static bool IsGiftsCategory(string? value) => NormalizeText(value) == "regalos";

    private static string NormalizeToken(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
