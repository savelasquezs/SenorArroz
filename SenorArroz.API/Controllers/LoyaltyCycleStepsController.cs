using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.LoyaltyCycle.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Route("api/branches/{branchId:int}/loyalty-cycle")]
public class LoyaltyCycleStepsController : ControllerBase
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public LoyaltyCycleStepsController(IApplicationDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    [HttpGet]
    [Authorize(Roles = "Superadmin,Admin,Cashier")]
    public async Task<ActionResult<ApiResponse<List<LoyaltyCycleStepDto>>>> Get(
        int branchId,
        CancellationToken cancellationToken)
    {
        if (!CanReadBranch(branchId))
            return Forbid();

        var steps = await BaseQuery()
            .AsNoTracking()
            .Where(x => x.BranchId == branchId)
            .OrderBy(x => x.StepIndex)
            .ToListAsync(cancellationToken);

        return Ok(ApiResponse<List<LoyaltyCycleStepDto>>.SuccessResponse(
            steps.Select(ToDto).ToList(),
            "Ciclo de fidelizacion obtenido."));
    }

    [HttpPut]
    [Authorize(Roles = "Superadmin,Admin")]
    public async Task<ActionResult<ApiResponse<List<LoyaltyCycleStepDto>>>> SaveCycle(
        int branchId,
        [FromBody] List<UpsertLoyaltyCycleStepDto> dtos,
        CancellationToken cancellationToken)
    {
        if (!CanManageBranch(branchId))
            return Forbid();

        if (!await _db.Branches.AsNoTracking().AnyAsync(x => x.Id == branchId, cancellationToken))
            return NotFound(ApiResponse<List<LoyaltyCycleStepDto>>.ErrorResponse("Sucursal no encontrada."));

        if (dtos.Count == 0)
            return BadRequest(ApiResponse<List<LoyaltyCycleStepDto>>.ErrorResponse("Debe enviar al menos un paso del ciclo."));

        var duplicatedStep = dtos.GroupBy(x => x.StepIndex).FirstOrDefault(g => g.Count() > 1);
        if (duplicatedStep is not null)
            return BadRequest(ApiResponse<List<LoyaltyCycleStepDto>>.ErrorResponse($"El paso {duplicatedStep.Key} esta duplicado."));

        foreach (var dto in dtos)
        {
            var error = await ValidateStep(branchId, dto, cancellationToken);
            if (error is not null)
                return BadRequest(ApiResponse<List<LoyaltyCycleStepDto>>.ErrorResponse(error));
        }

        var existing = await _db.LoyaltyCycleSteps
            .Where(x => x.BranchId == branchId)
            .ToListAsync(cancellationToken);

        var incomingIndexes = dtos.Select(x => x.StepIndex).ToHashSet();
        foreach (var step in existing.Where(x => !incomingIndexes.Contains(x.StepIndex)))
            step.IsActive = false;

        foreach (var dto in dtos.OrderBy(x => x.StepIndex))
        {
            var step = existing.FirstOrDefault(x => x.StepIndex == dto.StepIndex);
            if (step is null)
            {
                step = new LoyaltyCycleStep { BranchId = branchId, StepIndex = dto.StepIndex };
                _db.LoyaltyCycleSteps.Add(step);
            }

            ApplyDto(step, dto);
        }

        await _db.SaveChangesAsync(cancellationToken);

        var saved = await BaseQuery()
            .AsNoTracking()
            .Where(x => x.BranchId == branchId)
            .OrderBy(x => x.StepIndex)
            .ToListAsync(cancellationToken);

        return Ok(ApiResponse<List<LoyaltyCycleStepDto>>.SuccessResponse(
            saved.Select(ToDto).ToList(),
            "Ciclo de fidelizacion guardado."));
    }

    [HttpPost("{stepId:int}")]
    [HttpPut("{stepId:int}")]
    [Authorize(Roles = "Superadmin,Admin")]
    public async Task<ActionResult<ApiResponse<LoyaltyCycleStepDto>>> UpsertStep(
        int branchId,
        int stepId,
        [FromBody] UpsertLoyaltyCycleStepDto dto,
        CancellationToken cancellationToken)
    {
        if (!CanManageBranch(branchId))
            return Forbid();

        if (!await _db.Branches.AsNoTracking().AnyAsync(x => x.Id == branchId, cancellationToken))
            return NotFound(ApiResponse<LoyaltyCycleStepDto>.ErrorResponse("Sucursal no encontrada."));

        var error = await ValidateStep(branchId, dto, cancellationToken);
        if (error is not null)
            return BadRequest(ApiResponse<LoyaltyCycleStepDto>.ErrorResponse(error));

        var step = await _db.LoyaltyCycleSteps
            .FirstOrDefaultAsync(x => x.Id == stepId && x.BranchId == branchId, cancellationToken)
            ?? await _db.LoyaltyCycleSteps
                .FirstOrDefaultAsync(x => x.BranchId == branchId && x.StepIndex == dto.StepIndex, cancellationToken);

        if (step is null)
        {
            step = new LoyaltyCycleStep { BranchId = branchId };
            _db.LoyaltyCycleSteps.Add(step);
        }

        var stepIndexInUse = await _db.LoyaltyCycleSteps
            .AsNoTracking()
            .AnyAsync(x => x.BranchId == branchId && x.StepIndex == dto.StepIndex && x.Id != step.Id, cancellationToken);
        if (stepIndexInUse)
            return BadRequest(ApiResponse<LoyaltyCycleStepDto>.ErrorResponse("Ya existe otro paso con ese StepIndex."));

        ApplyDto(step, dto);
        await _db.SaveChangesAsync(cancellationToken);

        var saved = await BaseQuery()
            .AsNoTracking()
            .FirstAsync(x => x.Id == step.Id, cancellationToken);

        return Ok(ApiResponse<LoyaltyCycleStepDto>.SuccessResponse(ToDto(saved), "Paso de fidelizacion guardado."));
    }

    [HttpDelete("{stepId:int}")]
    [Authorize(Roles = "Superadmin,Admin")]
    public async Task<ActionResult<ApiResponse<LoyaltyCycleStepDto?>>> DisableStep(
        int branchId,
        int stepId,
        CancellationToken cancellationToken)
    {
        if (!CanManageBranch(branchId))
            return Forbid();

        var step = await _db.LoyaltyCycleSteps
            .FirstOrDefaultAsync(x => x.Id == stepId && x.BranchId == branchId, cancellationToken);

        if (step is null)
            return NotFound(ApiResponse<LoyaltyCycleStepDto?>.ErrorResponse("Paso de fidelizacion no encontrado."));

        step.IsActive = false;
        await _db.SaveChangesAsync(cancellationToken);

        var saved = await BaseQuery()
            .AsNoTracking()
            .FirstAsync(x => x.Id == step.Id, cancellationToken);

        return Ok(ApiResponse<LoyaltyCycleStepDto?>.SuccessResponse(ToDto(saved), "Paso de fidelizacion desactivado."));
    }

    private IQueryable<LoyaltyCycleStep> BaseQuery() =>
        _db.LoyaltyCycleSteps
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

    private async Task<string?> ValidateStep(
        int branchId,
        UpsertLoyaltyCycleStepDto dto,
        CancellationToken cancellationToken)
    {
        if (dto.StepIndex <= 0)
            return "StepIndex debe ser mayor que cero.";
        if (string.IsNullOrWhiteSpace(dto.RewardLabel))
            return "RewardLabel es requerido.";
        if (!TryParseName(dto.RewardType, out LoyaltyRewardType rewardType))
            return "RewardType debe ser GiftProduct, FreeDelivery o PercentageDiscount.";

        if (rewardType == LoyaltyRewardType.GiftProduct)
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

        if (rewardType == LoyaltyRewardType.FreeDelivery)
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

    private static void ApplyDto(LoyaltyCycleStep step, UpsertLoyaltyCycleStepDto dto)
    {
        TryParseName(dto.RewardType, out LoyaltyRewardType rewardType);
        step.StepIndex = dto.StepIndex;
        step.StepName = string.IsNullOrWhiteSpace(dto.StepName) ? null : dto.StepName.Trim();
        step.RewardLabel = dto.RewardLabel.Trim();
        step.RewardType = rewardType;
        step.GiftProductId = rewardType == LoyaltyRewardType.GiftProduct ? dto.GiftProductId : null;
        step.DiscountPercentage = rewardType == LoyaltyRewardType.PercentageDiscount ? dto.DiscountPercentage : null;
        step.IsActive = dto.IsActive;
    }

    private static LoyaltyCycleStepDto ToDto(LoyaltyCycleStep step)
    {
        return new LoyaltyCycleStepDto
        {
            Id = step.Id,
            BranchId = step.BranchId,
            StepIndex = step.StepIndex,
            StepName = step.StepName,
            RewardLabel = step.RewardLabel,
            RewardType = step.RewardType?.ToString(),
            GiftProductId = step.GiftProductId,
            GiftProductName = step.GiftProduct?.Name,
            GiftProductCategoryName = step.GiftProduct?.Category?.Name,
            DiscountPercentage = step.DiscountPercentage,
            IsActive = step.IsActive,
            CreatedAt = step.CreatedAt,
            UpdatedAt = step.UpdatedAt
        };
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
