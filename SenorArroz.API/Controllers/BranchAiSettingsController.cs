using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Models;
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
    private readonly IAiChatProviderResolver _aiChatProviderResolver;
    private readonly IAiApiKeyProvider _apiKeys;
    private readonly ILogger<BranchAiSettingsController> _logger;
    private readonly IWhatsAppSystemPromptBuilder _promptBuilder;
    private readonly IAgentToolCatalog _toolCatalog;
    private readonly IAiToolSchemaValidator _toolSchemaValidator;

    public BranchAiSettingsController(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IClock clock,
        IAiProviderResolver aiProviderResolver,
        IAiChatProviderResolver aiChatProviderResolver,
        IAiApiKeyProvider apiKeys,
        ILogger<BranchAiSettingsController> logger, IWhatsAppSystemPromptBuilder promptBuilder,
        IAgentToolCatalog toolCatalog, IAiToolSchemaValidator toolSchemaValidator)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _aiProviderResolver = aiProviderResolver;
        _aiChatProviderResolver = aiChatProviderResolver;
        _apiKeys = apiKeys;
        _logger = logger;
        _promptBuilder = promptBuilder;
        _toolCatalog = toolCatalog;
        _toolSchemaValidator = toolSchemaValidator;
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

        if (setting is null)
        {
            setting = new BranchAiSetting { BranchId = branchId };
            _db.BranchAiSettings.Add(setting);
        }

        var hasCriticalChanges =
            !string.Equals(setting.Provider, provider, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(setting.Model, model, StringComparison.Ordinal);

        setting.Provider = provider;
        setting.Model = model;
        setting.IsActive = dto.IsActive;
        setting.Temperature = dto.Temperature;
        setting.MaxContextMessages = dto.MaxContextMessages;
        setting.AssistantName = (dto.AssistantName ?? string.Empty).Trim();
        setting.PromptObjective = (dto.PromptObjective ?? string.Empty).Trim();
        setting.PromptPersonality = (dto.PromptPersonality ?? string.Empty).Trim();
        setting.PromptRequiredRules = (dto.PromptRequiredRules ?? string.Empty).Trim();
        setting.PromptFixedBranchInfo = (dto.PromptFixedBranchInfo ?? string.Empty).Trim();
        setting.PromptAdditionalInstructions = (dto.PromptAdditionalInstructions ?? string.Empty).Trim();
        setting.TransferMessage = (dto.TransferMessage ?? string.Empty).Trim();

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
        var apiKey = _apiKeys.GetApiKey(setting.Provider);
        if (validationError is null && string.IsNullOrWhiteSpace(apiKey))
            validationError = $"Falta la variable de entorno {_apiKeys.GetEnvironmentVariableName(setting.Provider)}.";
        setting.LastTestedAt = _clock.UtcNow;

        if (validationError is not null)
        {
            setting.IsVerified = false;
            await _db.SaveChangesAsync(cancellationToken);

            return BadRequest(ApiResponse<AiTestConnectionResultDto>.ErrorResponse(validationError));
        }

        var modelsResult = await _aiProviderResolver.ListModelsAsync(setting.Provider, apiKey!, cancellationToken);
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

        var chatProvider = _aiChatProviderResolver.Resolve(setting.Provider);
        if (chatProvider is null)
        {
            setting.IsVerified = false;
            await _db.SaveChangesAsync(cancellationToken);
            return BadRequest(ApiResponse<AiTestConnectionResultDto>.ErrorResponse("El proveedor no soporta generación de conversación."));
        }

        var schemaError = _toolSchemaValidator.Validate(_toolCatalog.All);
        if (schemaError is not null)
        {
            setting.IsVerified = false;
            await _db.SaveChangesAsync(cancellationToken);
            return BadRequest(ApiResponse<AiTestConnectionResultDto>.ErrorResponse(schemaError.ToString()));
        }
        var generation = await chatProvider.GenerateAsync(new(
            setting.Model,
            apiKey!,
            [new("user", "Responde únicamente OK y no llames herramientas.")],
            _toolCatalog.All,
            setting.Temperature), cancellationToken);
        if (generation.Error is not null
            || (string.IsNullOrWhiteSpace(generation.Text) && generation.ToolCalls.Count == 0))
        {
            setting.IsVerified = false;
            await _db.SaveChangesAsync(cancellationToken);
            return BadRequest(ApiResponse<AiTestConnectionResultDto>.ErrorResponse(
                generation.Error ?? "El modelo no produjo una respuesta de prueba compatible con herramientas."));
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

        var apiKey = _apiKeys.GetApiKey(provider);
        if (string.IsNullOrWhiteSpace(apiKey))
            return BadRequest(ApiResponse<AiProviderModelsResultDto>.ErrorResponse(
                $"Falta la variable de entorno {_apiKeys.GetEnvironmentVariableName(provider)}."));

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

    [HttpPost("prompt-preview")]
    public async Task<ActionResult<ApiResponse<PromptPreviewDto>>> Preview(int branchId, [FromBody] UpsertBranchAiSettingDto dto, CancellationToken cancellationToken)
    {
        if (!CanAccessBranch(branchId)) return Forbid();
        if (!await _db.Branches.AsNoTracking().AnyAsync(x => x.Id == branchId, cancellationToken))
            return NotFound(ApiResponse<PromptPreviewDto>.ErrorResponse("Sucursal no encontrada."));
        var promptError = ValidatePromptLengths(dto); if (promptError is not null) return BadRequest(ApiResponse<PromptPreviewDto>.ErrorResponse(promptError));
        var draft = new WhatsAppPromptConfiguration(dto.AssistantName,dto.PromptObjective,dto.PromptPersonality,dto.PromptRequiredRules,dto.PromptFixedBranchInfo,dto.PromptAdditionalInstructions);
        return Ok(ApiResponse<PromptPreviewDto>.SuccessResponse(new(await _promptBuilder.Build(branchId, draft, cancellationToken))));
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
        var promptError = ValidatePromptLengths(dto); if (promptError is not null) return promptError;
        return null;
    }

    private static string? ValidatePromptLengths(UpsertBranchAiSettingDto dto)
    {
        if ((dto.AssistantName?.Length ?? 0)>150) return "AssistantName no puede superar 150 caracteres.";
        if ((dto.PromptObjective?.Length ?? 0)>2000||(dto.PromptPersonality?.Length ?? 0)>2000) return "Objetivo y personalidad no pueden superar 2.000 caracteres.";
        if ((dto.PromptRequiredRules?.Length ?? 0)>8000||(dto.PromptFixedBranchInfo?.Length ?? 0)>8000||(dto.PromptAdditionalInstructions?.Length ?? 0)>8000) return "Los bloques extensos del prompt no pueden superar 8.000 caracteres.";
        if ((dto.TransferMessage?.Length ?? 0)>1000) return "TransferMessage no puede superar 1.000 caracteres.";
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
        return null;
    }

    private static string NormalizeProvider(string? provider)
    {
        var value = provider?.Trim().ToLowerInvariant() ?? string.Empty;
        return value is "google_gemini" or "google-gemini" or "google gemini" ? "gemini" : value;
    }

    private BranchAiSettingDto ToDto(BranchAiSetting? setting, int branchId)
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
            ApiKeyConfigured = !string.IsNullOrWhiteSpace(_apiKeys.GetApiKey(setting.Provider)),
            ApiKeyMasked = !string.IsNullOrWhiteSpace(_apiKeys.GetApiKey(setting.Provider)) ? "Variable de entorno" : null,
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
