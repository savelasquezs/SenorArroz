using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.BranchAiSettings.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Authorize(Roles = "Superadmin, Admin")]
[Route("api/branches/{branchId:int}/ai-settings")]
public class BranchAiSettingsController : ControllerBase
{
    private static readonly HashSet<string> AllowedProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        "openai",
        "gemini"
    };

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IAiProviderResolver _aiProviderResolver;
    private readonly ILogger<BranchAiSettingsController> _logger;
    private readonly IWhatsAppSystemPromptBuilder _promptBuilder;

    public BranchAiSettingsController(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IClock clock,
        IAiProviderResolver aiProviderResolver,
        ILogger<BranchAiSettingsController> logger, IWhatsAppSystemPromptBuilder promptBuilder)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _aiProviderResolver = aiProviderResolver;
        _logger = logger;
        _promptBuilder = promptBuilder;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<BranchAiSettingDto>>> Get(int branchId, CancellationToken cancellationToken)
    {
        if (!CanAccessBranch(branchId))
            return Forbid();

        var setting = await _db.BranchAiSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.BranchId == branchId, cancellationToken);

        return Ok(ApiResponse<BranchAiSettingDto>.SuccessResponse(
            ToDto(setting, branchId),
            "Configuracion de IA obtenida."));
    }

    [HttpPut]
    public async Task<ActionResult<ApiResponse<BranchAiSettingDto>>> Upsert(
        int branchId,
        [FromBody] UpsertBranchAiSettingDto dto,
        CancellationToken cancellationToken)
    {
        if (!CanAccessBranch(branchId))
            return Forbid();

        if (!await _db.Branches.AsNoTracking().AnyAsync(x => x.Id == branchId, cancellationToken))
            return NotFound(ApiResponse<BranchAiSettingDto>.ErrorResponse("Sucursal no encontrada."));

        var setting = await _db.BranchAiSettings
            .FirstOrDefaultAsync(x => x.BranchId == branchId, cancellationToken);

        var validationError = Validate(dto, setting);
        if (validationError is not null)
            return BadRequest(ApiResponse<BranchAiSettingDto>.ErrorResponse(validationError));

        var provider = NormalizeProvider(dto.Provider);
        var model = dto.Model.Trim();
        var apiKey = dto.ApiKey?.Trim();

        if (setting is null)
        {
            setting = new BranchAiSetting { BranchId = branchId };
            _db.BranchAiSettings.Add(setting);
        }

        var hasCriticalChanges =
            !string.Equals(setting.Provider, provider, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(setting.Model, model, StringComparison.Ordinal)
            || !string.IsNullOrWhiteSpace(apiKey);

        setting.Provider = provider;
        setting.Model = model;
        if (!string.IsNullOrWhiteSpace(apiKey))
            setting.ApiKey = apiKey;
        setting.IsActive = dto.IsActive;
        setting.Temperature = dto.Temperature;
        setting.MaxContextMessages = dto.MaxContextMessages;
        setting.AssistantName = dto.AssistantName.Trim();
        setting.PromptObjective = dto.PromptObjective.Trim();
        setting.PromptPersonality = dto.PromptPersonality.Trim();
        setting.PromptRequiredRules = dto.PromptRequiredRules.Trim();
        setting.PromptFixedBranchInfo = dto.PromptFixedBranchInfo.Trim();
        setting.PromptAdditionalInstructions = dto.PromptAdditionalInstructions.Trim();
        setting.TransferMessage = dto.TransferMessage.Trim();

        if (hasCriticalChanges)
        {
            setting.IsVerified = false;
            setting.LastTestedAt = null;
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("AI settings saved for branch {BranchId}. Verified: {IsVerified}", branchId, setting.IsVerified);
        return Ok(ApiResponse<BranchAiSettingDto>.SuccessResponse(ToDto(setting, branchId), "Configuracion de IA guardada."));
    }

    [HttpPost("test-connection")]
    public async Task<ActionResult<ApiResponse<AiTestConnectionResultDto>>> TestConnection(
        int branchId,
        CancellationToken cancellationToken)
    {
        if (!CanAccessBranch(branchId))
            return Forbid();

        var setting = await _db.BranchAiSettings
            .FirstOrDefaultAsync(x => x.BranchId == branchId, cancellationToken);

        if (setting is null)
            return NotFound(ApiResponse<AiTestConnectionResultDto>.ErrorResponse("No hay configuracion de IA para esta sucursal."));

        var validationError = ValidatePersistedSetting(setting);
        setting.LastTestedAt = _clock.UtcNow;

        if (validationError is not null)
        {
            setting.IsVerified = false;
            await _db.SaveChangesAsync(cancellationToken);

            return BadRequest(ApiResponse<AiTestConnectionResultDto>.ErrorResponse(validationError));
        }

        var modelsResult = await _aiProviderResolver.ListModelsAsync(setting.Provider, setting.ApiKey, cancellationToken);
        if (!modelsResult.Success)
        {
            setting.IsVerified = false;
            await _db.SaveChangesAsync(cancellationToken);

            return BadRequest(ApiResponse<AiTestConnectionResultDto>.ErrorResponse(
                modelsResult.ErrorMessage ?? "No se pudo validar la conexion con el proveedor de IA."));
        }

        if (!modelsResult.Models.Any(x => string.Equals(x.Id, setting.Model, StringComparison.OrdinalIgnoreCase)))
        {
            setting.IsVerified = false;
            await _db.SaveChangesAsync(cancellationToken);

            return BadRequest(ApiResponse<AiTestConnectionResultDto>.ErrorResponse(
                "El modelo seleccionado no esta disponible para la API Key y proveedor configurados."));
        }

        setting.IsVerified = true;
        await _db.SaveChangesAsync(cancellationToken);

        var response = new AiTestConnectionResultDto
        {
            Success = true,
            Message = "Conexion de IA validada correctamente.",
            Setting = ToDto(setting, branchId)
        };

        _logger.LogInformation("AI settings test succeeded for branch {BranchId}", branchId);
        return Ok(ApiResponse<AiTestConnectionResultDto>.SuccessResponse(response, response.Message));
    }

    [HttpPost("models")]
    public async Task<ActionResult<ApiResponse<AiProviderModelsResultDto>>> GetProviderModels(
        int branchId,
        [FromBody] AiModelLookupDto dto,
        CancellationToken cancellationToken)
    {
        if (!CanAccessBranch(branchId))
            return Forbid();

        if (!await _db.Branches.AsNoTracking().AnyAsync(x => x.Id == branchId, cancellationToken))
            return NotFound(ApiResponse<AiProviderModelsResultDto>.ErrorResponse("Sucursal no encontrada."));

        var provider = NormalizeProvider(dto.Provider);
        if (string.IsNullOrWhiteSpace(provider))
            return BadRequest(ApiResponse<AiProviderModelsResultDto>.ErrorResponse("Provider es requerido."));
        if (!AllowedProviders.Contains(provider))
            return BadRequest(ApiResponse<AiProviderModelsResultDto>.ErrorResponse("Provider debe ser openai o gemini."));

        var apiKey = dto.ApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            var setting = await _db.BranchAiSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.BranchId == branchId, cancellationToken);

            if (setting is not null
                && string.Equals(NormalizeProvider(setting.Provider), provider, StringComparison.OrdinalIgnoreCase))
            {
                apiKey = setting.ApiKey;
            }
        }

        if (string.IsNullOrWhiteSpace(apiKey))
            return BadRequest(ApiResponse<AiProviderModelsResultDto>.ErrorResponse("ApiKey es requerida para consultar modelos."));

        var result = await _aiProviderResolver.ListModelsAsync(provider, apiKey, cancellationToken);
        if (!result.Success)
            return BadRequest(ApiResponse<AiProviderModelsResultDto>.ErrorResponse(
                result.ErrorMessage ?? "No se pudieron consultar los modelos disponibles."));

        var response = new AiProviderModelsResultDto
        {
            Provider = provider,
            Models = result.Models
                .Select(x => new AiProviderModelDto { Id = x.Id, DisplayName = x.DisplayName })
                .ToList()
        };

        return Ok(ApiResponse<AiProviderModelsResultDto>.SuccessResponse(response, "Modelos de IA obtenidos."));
    }

    [HttpGet("prompt-preview")]
    public async Task<ActionResult<ApiResponse<PromptPreviewDto>>> Preview(int branchId, CancellationToken cancellationToken)
    {
        if (!CanAccessBranch(branchId)) return Forbid();
        if (!await _db.Branches.AsNoTracking().AnyAsync(x => x.Id == branchId, cancellationToken))
            return NotFound(ApiResponse<PromptPreviewDto>.ErrorResponse("Sucursal no encontrada."));
        return Ok(ApiResponse<PromptPreviewDto>.SuccessResponse(new(await _promptBuilder.Build(branchId, cancellationToken))));
    }

    private bool CanAccessBranch(int branchId)
    {
        if (Roles.IsSuperadmin(_currentUser.Role))
            return true;
        return Roles.IsAdmin(_currentUser.Role) && _currentUser.BranchId == branchId;
    }

    private static string? Validate(UpsertBranchAiSettingDto dto, BranchAiSetting? existingSetting)
    {
        var provider = NormalizeProvider(dto.Provider);
        if (string.IsNullOrWhiteSpace(provider))
            return "Provider es requerido.";
        if (!AllowedProviders.Contains(provider))
            return "Provider debe ser openai o gemini.";
        if (string.IsNullOrWhiteSpace(dto.Model))
            return "Model es requerido.";
        if (dto.MaxContextMessages <= 0)
            return "MaxContextMessages debe ser mayor que cero.";
        if (dto.Temperature is < 0 or > 2)
            return "Temperature debe estar entre 0 y 2.";
        if (string.IsNullOrWhiteSpace(dto.ApiKey)
            && (existingSetting is null
                || !string.Equals(NormalizeProvider(existingSetting.Provider), provider, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(existingSetting.ApiKey)))
        {
            return "ApiKey es requerida para este provider.";
        }

        return null;
    }

    private static string? ValidatePersistedSetting(BranchAiSetting setting)
    {
        if (string.IsNullOrWhiteSpace(setting.Provider))
            return "Provider es requerido.";
        if (!AllowedProviders.Contains(setting.Provider))
            return "Provider debe ser openai o gemini.";
        if (string.IsNullOrWhiteSpace(setting.Model))
            return "Model es requerido.";
        if (string.IsNullOrWhiteSpace(setting.ApiKey))
            return "ApiKey es requerida para este provider.";

        return null;
    }

    private static string NormalizeProvider(string? provider)
    {
        var value = provider?.Trim().ToLowerInvariant() ?? string.Empty;
        return value is "google_gemini" or "google-gemini" or "google gemini" ? "gemini" : value;
    }

    private static BranchAiSettingDto ToDto(BranchAiSetting? setting, int branchId)
    {
        if (setting is null)
        {
            return new BranchAiSettingDto
            {
                BranchId = branchId,
                Status = "not_configured"
            };
        }

        return new BranchAiSettingDto
        {
            Id = setting.Id,
            BranchId = setting.BranchId,
            Provider = setting.Provider,
            Model = setting.Model,
            ApiKeyConfigured = !string.IsNullOrWhiteSpace(setting.ApiKey),
            ApiKeyMasked = string.IsNullOrWhiteSpace(setting.ApiKey) ? null : "\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022",
            IsActive = setting.IsActive,
            Temperature = setting.Temperature,
            MaxContextMessages = setting.MaxContextMessages,
            LastTestedAt = setting.LastTestedAt,
            IsVerified = setting.IsVerified,
            CreatedAt = setting.CreatedAt,
            UpdatedAt = setting.UpdatedAt,
            Status = !setting.IsActive
                ? "inactive"
                : setting.IsVerified
                    ? "connected"
                    : "configured_unverified",
            AssistantName = setting.AssistantName,
            PromptObjective = setting.PromptObjective,
            PromptPersonality = setting.PromptPersonality,
            PromptRequiredRules = setting.PromptRequiredRules,
            PromptFixedBranchInfo = setting.PromptFixedBranchInfo,
            PromptAdditionalInstructions = setting.PromptAdditionalInstructions,
            TransferMessage = setting.TransferMessage
        };
    }
}
