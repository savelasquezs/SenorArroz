using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Authorize(Roles = "Superadmin, Admin")]
[Route("api/branches/{branchId:int}/whatsapp-settings")]
public class BranchWhatsAppSettingsController : ControllerBase
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IWhatsAppCloudClient _whatsAppCloudClient;
    private readonly ILogger<BranchWhatsAppSettingsController> _logger;

    public BranchWhatsAppSettingsController(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IClock clock,
        IWhatsAppCloudClient whatsAppCloudClient,
        ILogger<BranchWhatsAppSettingsController> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _whatsAppCloudClient = whatsAppCloudClient;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<WhatsAppBranchSettingDto>>> Get(int branchId, CancellationToken cancellationToken)
    {
        if (!CanAccessBranch(branchId))
            return Forbid();

        var setting = await _db.WhatsAppBranchSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.BranchId == branchId, cancellationToken);

        return Ok(ApiResponse<WhatsAppBranchSettingDto>.SuccessResponse(
            ToDto(setting, branchId),
            "Configuración de WhatsApp obtenida."));
    }

    [HttpPost]
    public Task<ActionResult<ApiResponse<WhatsAppBranchSettingDto>>> CreateOrUpdate(
        int branchId,
        [FromBody] UpsertWhatsAppBranchSettingDto dto,
        CancellationToken cancellationToken) =>
        Upsert(branchId, dto, cancellationToken);

    [HttpPut]
    public async Task<ActionResult<ApiResponse<WhatsAppBranchSettingDto>>> Upsert(
        int branchId,
        [FromBody] UpsertWhatsAppBranchSettingDto dto,
        CancellationToken cancellationToken)
    {
        if (!CanAccessBranch(branchId))
            return Forbid();

        if (!await _db.Branches.AsNoTracking().AnyAsync(x => x.Id == branchId, cancellationToken))
            return NotFound(ApiResponse<WhatsAppBranchSettingDto>.ErrorResponse("Sucursal no encontrada."));

        var validationError = Validate(dto, requireToken: false);
        if (validationError is not null)
            return BadRequest(ApiResponse<WhatsAppBranchSettingDto>.ErrorResponse(validationError));

        var setting = await _db.WhatsAppBranchSettings
            .FirstOrDefaultAsync(x => x.BranchId == branchId, cancellationToken);

        if (setting is null)
        {
            if (string.IsNullOrWhiteSpace(dto.AccessToken))
                return BadRequest(ApiResponse<WhatsAppBranchSettingDto>.ErrorResponse("El Access Token es requerido para crear la configuración."));

            setting = new WhatsAppBranchSetting
            {
                BranchId = branchId,
                AccessToken = dto.AccessToken.Trim()
            };
            _db.WhatsAppBranchSettings.Add(setting);
        }

        var hasCriticalChanges =
            !string.Equals(setting.PhoneNumberId, dto.PhoneNumberId.Trim(), StringComparison.Ordinal)
            || !string.Equals(setting.BusinessAccountId, dto.BusinessAccountId.Trim(), StringComparison.Ordinal)
            || !string.Equals(setting.WebhookVerifyToken, dto.WebhookVerifyToken.Trim(), StringComparison.Ordinal)
            || !string.IsNullOrWhiteSpace(dto.AccessToken);

        setting.PhoneNumberId = dto.PhoneNumberId.Trim();
        setting.BusinessAccountId = dto.BusinessAccountId.Trim();
        setting.DisplayPhoneNumber = dto.DisplayPhoneNumber.Trim();
        if (!string.IsNullOrWhiteSpace(dto.AccessToken))
            setting.AccessToken = dto.AccessToken.Trim();
        setting.WebhookVerifyToken = dto.WebhookVerifyToken.Trim();
        setting.AppSecret = string.IsNullOrWhiteSpace(dto.AppSecret) ? null : dto.AppSecret.Trim();
        setting.IsActive = dto.IsActive;
        if (hasCriticalChanges)
        {
            setting.IsVerified = false;
            setting.LastVerifiedAt = null;
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("WhatsApp settings saved for branch {BranchId}. Verified: {IsVerified}", branchId, setting.IsVerified);
        return Ok(ApiResponse<WhatsAppBranchSettingDto>.SuccessResponse(ToDto(setting, branchId), "Configuración de WhatsApp guardada."));
    }

    [HttpPost("test-connection")]
    public async Task<ActionResult<ApiResponse<WhatsAppTestConnectionResultDto>>> TestConnection(
        int branchId,
        CancellationToken cancellationToken)
    {
        if (!CanAccessBranch(branchId))
            return Forbid();

        var setting = await _db.WhatsAppBranchSettings
            .FirstOrDefaultAsync(x => x.BranchId == branchId, cancellationToken);

        if (setting is null)
            return NotFound(ApiResponse<WhatsAppTestConnectionResultDto>.ErrorResponse("No hay configuración de WhatsApp para esta sucursal."));

        var result = await _whatsAppCloudClient.TestConnectionAsync(setting.PhoneNumberId, setting.AccessToken, cancellationToken);
        if (!result.Success)
        {
            setting.IsVerified = false;
            setting.LastVerifiedAt = null;
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogWarning("WhatsApp test connection failed for branch {BranchId}: {Error}", branchId, result.ErrorMessage);
            return BadRequest(ApiResponse<WhatsAppTestConnectionResultDto>.ErrorResponse(
                result.ErrorMessage ?? "La conexión con Meta falló."));
        }

        setting.IsVerified = true;
        setting.IsActive = true;
        setting.LastVerifiedAt = _clock.UtcNow;
        if (!string.IsNullOrWhiteSpace(result.DisplayPhoneNumber))
            setting.DisplayPhoneNumber = result.DisplayPhoneNumber;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("WhatsApp test connection succeeded for branch {BranchId}", branchId);
        var response = new WhatsAppTestConnectionResultDto
        {
            Success = true,
            Message = "Conexión verificada correctamente.",
            Setting = ToDto(setting, branchId)
        };
        return Ok(ApiResponse<WhatsAppTestConnectionResultDto>.SuccessResponse(response, response.Message));
    }

    private bool CanAccessBranch(int branchId)
    {
        if (Roles.IsSuperadmin(_currentUser.Role))
            return true;
        return Roles.IsAdmin(_currentUser.Role) && _currentUser.BranchId == branchId;
    }

    private static string? Validate(UpsertWhatsAppBranchSettingDto dto, bool requireToken)
    {
        if (string.IsNullOrWhiteSpace(dto.PhoneNumberId))
            return "Phone Number ID es requerido.";
        if (string.IsNullOrWhiteSpace(dto.BusinessAccountId))
            return "Business Account ID es requerido.";
        if (string.IsNullOrWhiteSpace(dto.DisplayPhoneNumber))
            return "Display Phone Number es requerido.";
        if (requireToken && string.IsNullOrWhiteSpace(dto.AccessToken))
            return "Access Token es requerido.";
        if (string.IsNullOrWhiteSpace(dto.WebhookVerifyToken))
            return "Webhook Verify Token es requerido.";
        return null;
    }

    private static WhatsAppBranchSettingDto ToDto(WhatsAppBranchSetting? setting, int branchId)
    {
        if (setting is null)
        {
            return new WhatsAppBranchSettingDto
            {
                BranchId = branchId,
                Status = "not_configured"
            };
        }

        return new WhatsAppBranchSettingDto
        {
            Id = setting.Id,
            BranchId = setting.BranchId,
            PhoneNumberId = setting.PhoneNumberId,
            BusinessAccountId = setting.BusinessAccountId,
            DisplayPhoneNumber = setting.DisplayPhoneNumber,
            AccessTokenConfigured = !string.IsNullOrWhiteSpace(setting.AccessToken),
            AccessTokenMasked = string.IsNullOrWhiteSpace(setting.AccessToken) ? null : "••••••••",
            WebhookVerifyToken = setting.WebhookVerifyToken,
            AppSecretConfigured = !string.IsNullOrWhiteSpace(setting.AppSecret),
            IsActive = setting.IsActive,
            IsVerified = setting.IsVerified,
            LastVerifiedAt = setting.LastVerifiedAt,
            CreatedAt = setting.CreatedAt,
            UpdatedAt = setting.UpdatedAt,
            Status = !setting.IsActive
                ? "configured_inactive"
                : setting.IsVerified
                    ? "connected"
                    : "configured_unverified"
        };
    }
}
