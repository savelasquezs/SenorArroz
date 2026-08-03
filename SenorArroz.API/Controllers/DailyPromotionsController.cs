using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.DailyPromotions.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Route("api/branches/{branchId:int}/daily-promotion")]
public class DailyPromotionsController : ControllerBase
{
    private const int CashierPromotionStartHourColombia = 5;
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public DailyPromotionsController(IApplicationDbContext db, ICurrentUser currentUser, IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    [HttpGet("active")]
    [Authorize(Roles = "Superadmin,Admin,Cashier")]
    public async Task<ActionResult<ApiResponse<DailyPromotionDto?>>> GetActive(
        int branchId,
        CancellationToken cancellationToken)
    {
        if (!CanReadBranch(branchId))
            return Forbid();

        var now = _clock.UtcNow;
        var promotion = await BaseQuery()
            .AsNoTracking()
            .Where(x => x.BranchId == branchId
                && x.IsActive
                && x.StartsAt <= now
                && (x.EndsAt == null || x.EndsAt > now))
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return Ok(ApiResponse<DailyPromotionDto?>.SuccessResponse(
            promotion is null ? null : ToDto(promotion, now, CanManagePromotion(promotion, branchId)),
            promotion is null ? "No hay promocion activa vigente." : "Promocion activa obtenida."));
    }

    [HttpGet]
    [Authorize(Roles = "Superadmin,Admin,Cashier")]
    public async Task<ActionResult<ApiResponse<DailyPromotionDto?>>> GetCurrent(
        int branchId,
        CancellationToken cancellationToken)
    {
        if (!CanReadBranch(branchId))
            return Forbid();

        var now = _clock.UtcNow;
        var (todayStartUtc, tomorrowStartUtc) = TodayBounds(now);
        var activeToday = await BaseQuery()
            .AsNoTracking()
            .Where(x => x.BranchId == branchId
                && x.IsActive
                && x.StartsAt < tomorrowStartUtc
                && (x.EndsAt == null || x.EndsAt > todayStartUtc))
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var promotion = activeToday ?? await BaseQuery()
            .AsNoTracking()
            .Where(x => x.BranchId == branchId)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return Ok(ApiResponse<DailyPromotionDto?>.SuccessResponse(
            promotion is null ? null : ToDto(promotion, now, CanManagePromotion(activeToday, branchId)),
            promotion is null ? "No hay promocion configurada." : "Promocion obtenida."));
    }

    [HttpPut]
    [Authorize(Roles = "Superadmin,Admin,Cashier")]
    public async Task<ActionResult<ApiResponse<DailyPromotionDto>>> Upsert(
        int branchId,
        [FromBody] UpsertDailyPromotionDto dto,
        CancellationToken cancellationToken)
    {
        if (!CanManageBranch(branchId))
            return Forbid();

        if (!await _db.Branches.AsNoTracking().AnyAsync(x => x.Id == branchId, cancellationToken))
            return NotFound(ApiResponse<DailyPromotionDto>.ErrorResponse("Sucursal no encontrada."));

        var now = _clock.UtcNow;
        var (todayStartUtc, tomorrowStartUtc) = TodayBounds(now);
        var isCashier = Roles.IsCashier(_currentUser.Role);
        if (isCashier)
        {
            dto.StartsAt = todayStartUtc.AddHours(CashierPromotionStartHourColombia);
            dto.EndsAt = tomorrowStartUtc.AddTicks(-1);
        }

        var parsed = ParsePromotionInput(dto);
        if (parsed.Error is not null)
            return BadRequest(ApiResponse<DailyPromotionDto>.ErrorResponse(parsed.Error));

        var validationError = await ValidateBusinessRules(branchId, dto, parsed.Type!.Value, parsed.Scope, cancellationToken);
        if (validationError is not null)
            return BadRequest(ApiResponse<DailyPromotionDto>.ErrorResponse(validationError));

        DailyPromotion? promotion;
        if (isCashier)
        {
            var activeToday = await ActiveForDayQuery(branchId, todayStartUtc, tomorrowStartUtc)
                .Include(x => x.DiscountProducts)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (activeToday is not null && activeToday.CreatedByUserId != _currentUser.Id)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<DailyPromotionDto>.ErrorResponse(
                    "Ya existe una promocion activa para hoy creada por otro usuario."));
            }

            promotion = activeToday ?? await _db.DailyPromotions
                .Include(x => x.DiscountProducts)
                .Where(x => x.BranchId == branchId
                    && x.CreatedByUserId == _currentUser.Id
                    && x.StartsAt < tomorrowStartUtc
                    && (x.EndsAt == null || x.EndsAt > todayStartUtc))
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (promotion is null && !dto.IsActive)
            {
                return BadRequest(ApiResponse<DailyPromotionDto>.ErrorResponse(
                    "La nueva promocion del cajero debe quedar activa."));
            }
        }
        else
        {
            promotion = await ActiveForDayQuery(branchId, todayStartUtc, tomorrowStartUtc)
                .Include(x => x.DiscountProducts)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken)
                ?? await _db.DailyPromotions
                    .Include(x => x.DiscountProducts)
                    .Where(x => x.BranchId == branchId)
                    .OrderByDescending(x => x.CreatedAt)
                    .FirstOrDefaultAsync(cancellationToken);
        }

        var isNewPromotion = promotion is null;
        promotion ??= new DailyPromotion
        {
            BranchId = branchId,
            CreatedByUserId = _currentUser.Id
        };

        if (dto.IsActive)
        {
            if (isCashier)
            {
                var conflictingPromotionExists = await ActiveForDayQuery(branchId, todayStartUtc, tomorrowStartUtc)
                    .AnyAsync(x => x.Id != promotion.Id, cancellationToken);
                if (conflictingPromotionExists)
                {
                    return Conflict(ApiResponse<DailyPromotionDto>.ErrorResponse(
                        "Ya existe una promocion activa para hoy."));
                }
            }
            else
            {
                var activePromotions = await _db.DailyPromotions
                    .Where(x => x.BranchId == branchId && x.IsActive && x.Id != promotion.Id)
                    .ToListAsync(cancellationToken);

                foreach (var active in activePromotions)
                    active.IsActive = false;
            }
        }

        promotion.Type = parsed.Type.Value;
        promotion.GiftProductId = parsed.Type == DailyPromotionType.GiftProduct ? dto.GiftProductId : null;
        promotion.DiscountPercentage = parsed.Type == DailyPromotionType.PercentageDiscount ? dto.DiscountPercentage : null;
        promotion.DiscountScope = parsed.Type == DailyPromotionType.PercentageDiscount ? parsed.Scope : null;
        promotion.MinimumOrderValue = dto.MinimumOrderValue;
        promotion.IsActive = dto.IsActive;
        promotion.StartsAt = dto.StartsAt;
        promotion.EndsAt = dto.EndsAt;

        if (isNewPromotion)
            _db.DailyPromotions.Add(promotion);

        promotion.DiscountProducts.Clear();
        if (parsed.Type == DailyPromotionType.PercentageDiscount
            && parsed.Scope == DailyPromotionDiscountScope.SpecificProducts)
        {
            foreach (var productId in dto.DiscountProductIds.Distinct())
            {
                promotion.DiscountProducts.Add(new DailyPromotionProduct { ProductId = productId });
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        var saved = await BaseQuery()
            .AsNoTracking()
            .FirstAsync(x => x.Id == promotion.Id, cancellationToken);

        return Ok(ApiResponse<DailyPromotionDto>.SuccessResponse(ToDto(saved, _clock.UtcNow, true), "Promocion del dia guardada."));
    }

    [HttpDelete]
    [Authorize(Roles = "Superadmin,Admin,Cashier")]
    public async Task<ActionResult<ApiResponse<DailyPromotionDto?>>> Disable(
        int branchId,
        CancellationToken cancellationToken)
    {
        if (!CanManageBranch(branchId))
            return Forbid();

        var now = _clock.UtcNow;
        var (todayStartUtc, tomorrowStartUtc) = TodayBounds(now);
        var isCashier = Roles.IsCashier(_currentUser.Role);
        var promotion = await ActiveForDayQuery(branchId, todayStartUtc, tomorrowStartUtc)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (promotion is not null && isCashier && promotion.CreatedByUserId != _currentUser.Id)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<DailyPromotionDto?>.ErrorResponse(
                "Solo puedes desactivar una promocion creada por ti."));
        }

        if (promotion is null && !isCashier)
        {
            promotion = await _db.DailyPromotions
                .Where(x => x.BranchId == branchId)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (promotion is null)
            return Ok(ApiResponse<DailyPromotionDto?>.SuccessResponse(null, "No hay promocion para desactivar."));

        promotion.IsActive = false;
        await _db.SaveChangesAsync(cancellationToken);

        var saved = await BaseQuery()
            .AsNoTracking()
            .FirstAsync(x => x.Id == promotion.Id, cancellationToken);

        return Ok(ApiResponse<DailyPromotionDto?>.SuccessResponse(ToDto(saved, now, true), "Promocion del dia desactivada."));
    }

    private IQueryable<DailyPromotion> BaseQuery() =>
        _db.DailyPromotions
            .Include(x => x.GiftProduct)
                .ThenInclude(p => p!.Category)
            .Include(x => x.DiscountProducts)
                .ThenInclude(x => x.Product);

    private IQueryable<DailyPromotion> ActiveForDayQuery(int branchId, DateTime dayStartUtc, DateTime nextDayStartUtc) =>
        _db.DailyPromotions.Where(x => x.BranchId == branchId
            && x.IsActive
            && x.StartsAt < nextDayStartUtc
            && (x.EndsAt == null || x.EndsAt > dayStartUtc));

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
        return (Roles.IsAdmin(_currentUser.Role) || Roles.IsCashier(_currentUser.Role))
            && _currentUser.BranchId == branchId;
    }

    private static (DailyPromotionType? Type, DailyPromotionDiscountScope? Scope, string? Error) ParsePromotionInput(
        UpsertDailyPromotionDto dto)
    {
        if (!TryParseName(dto.Type, out DailyPromotionType type))
            return (null, null, "Type debe ser GiftProduct, FreeDelivery o PercentageDiscount.");

        DailyPromotionDiscountScope? scope = null;
        if (type == DailyPromotionType.PercentageDiscount)
        {
            if (!TryParseName(dto.DiscountScope, out DailyPromotionDiscountScope parsedScope))
                return (type, null, "DiscountScope debe ser AllProducts o SpecificProducts.");
            scope = parsedScope;
        }
        else if (!string.IsNullOrWhiteSpace(dto.DiscountScope))
        {
            return (type, null, "DiscountScope debe ser null para este tipo de promocion.");
        }

        return (type, scope, null);
    }

    private async Task<string?> ValidateBusinessRules(
        int branchId,
        UpsertDailyPromotionDto dto,
        DailyPromotionType type,
        DailyPromotionDiscountScope? scope,
        CancellationToken cancellationToken)
    {
        if (dto.StartsAt == default)
            return "StartsAt es requerido.";
        if (dto.EndsAt.HasValue && dto.EndsAt <= dto.StartsAt)
            return "EndsAt debe ser mayor que StartsAt.";
        if (dto.MinimumOrderValue is < 0)
            return "MinimumOrderValue no puede ser negativo.";

        var productIds = dto.DiscountProductIds.Distinct().ToList();

        if (type == DailyPromotionType.GiftProduct)
        {
            if (!dto.GiftProductId.HasValue)
                return "GiftProductId es requerido para Producto gratis.";
            if (dto.DiscountPercentage.HasValue)
                return "DiscountPercentage debe ser null para Producto gratis.";
            if (productIds.Count > 0)
                return "DiscountProducts debe estar vacio para Producto gratis.";

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

        if (type == DailyPromotionType.FreeDelivery)
        {
            if (dto.GiftProductId.HasValue)
                return "GiftProductId debe ser null para Domicilio gratis.";
            if (dto.DiscountPercentage.HasValue)
                return "DiscountPercentage debe ser null para Domicilio gratis.";
            if (productIds.Count > 0)
                return "DiscountProducts debe estar vacio para Domicilio gratis.";

            return null;
        }

        if (dto.GiftProductId.HasValue)
            return "GiftProductId debe ser null para Descuento porcentual.";
        if (!dto.DiscountPercentage.HasValue)
            return "DiscountPercentage es requerido para Descuento porcentual.";
        if (dto.DiscountPercentage <= 0 || dto.DiscountPercentage > 100)
            return "DiscountPercentage debe ser mayor a 0 y maximo 100.";
        if (!scope.HasValue)
            return "DiscountScope es requerido para Descuento porcentual.";

        if (scope == DailyPromotionDiscountScope.AllProducts)
        {
            if (productIds.Count > 0)
                return "DiscountProducts debe estar vacio cuando DiscountScope es AllProducts.";
            return null;
        }

        if (productIds.Count == 0)
            return "Debe seleccionar al menos un producto para DiscountScope SpecificProducts.";

        var validCount = await _db.Products
            .AsNoTracking()
            .Include(x => x.Category)
            .Where(x => productIds.Contains(x.Id) && x.Active && x.Category.BranchId == branchId)
            .CountAsync(cancellationToken);

        if (validCount != productIds.Count)
            return "Todos los productos del descuento deben existir, estar activos y pertenecer a la sucursal.";

        return null;
    }

    private static (DateTime TodayStartUtc, DateTime TomorrowStartUtc) TodayBounds(DateTime nowUtc) =>
        (
            ColombiaTimeHelper.GetTodayStartInUtcFromUtc(nowUtc),
            ColombiaTimeHelper.GetColombiaStartOfTomorrowUtcFromUtc(nowUtc)
        );

    private bool CanManagePromotion(DailyPromotion? activeToday, int branchId)
    {
        if (!CanManageBranch(branchId))
            return false;
        if (!Roles.IsCashier(_currentUser.Role))
            return true;
        return activeToday is null || activeToday.CreatedByUserId == _currentUser.Id;
    }

    private static DailyPromotionDto ToDto(DailyPromotion promotion, DateTime now, bool canManage)
    {
        return new DailyPromotionDto
        {
            Id = promotion.Id,
            BranchId = promotion.BranchId,
            CreatedByUserId = promotion.CreatedByUserId,
            Type = promotion.Type.ToString(),
            GiftProductId = promotion.GiftProductId,
            GiftProductName = promotion.GiftProduct?.Name,
            GiftProductCategoryName = promotion.GiftProduct?.Category?.Name,
            DiscountPercentage = promotion.DiscountPercentage,
            DiscountScope = promotion.DiscountScope?.ToString(),
            DiscountProducts = promotion.DiscountProducts
                .OrderBy(x => x.Product.Name)
                .Select(x => new DailyPromotionProductDto
                {
                    ProductId = x.ProductId,
                    ProductName = x.Product.Name
                })
                .ToList(),
            MinimumOrderValue = promotion.MinimumOrderValue,
            IsActive = promotion.IsActive,
            StartsAt = promotion.StartsAt,
            EndsAt = promotion.EndsAt,
            CreatedAt = promotion.CreatedAt,
            UpdatedAt = promotion.UpdatedAt,
            Status = GetStatus(promotion, now),
            CanManage = canManage
        };
    }

    private static string GetStatus(DailyPromotion promotion, DateTime now)
    {
        if (!promotion.IsActive)
            return "inactive";
        if (promotion.StartsAt > now)
            return "scheduled";
        if (promotion.EndsAt.HasValue && promotion.EndsAt <= now)
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
