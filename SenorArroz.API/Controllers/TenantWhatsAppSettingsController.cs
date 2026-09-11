using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Models;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Authorize(Roles = "Superadmin, Admin")]
[Route("api/tenant/whatsapp")]
public sealed class TenantWhatsAppSettingsController(
    IApplicationDbContext db,
    IWhatsAppCloudClient cloud,
    IClock clock,
    IAiApiKeyProvider aiApiKeys,
    IAiProviderResolver aiProviders,
    IAiChatProviderResolver aiChatProviders,
    IAgentToolCatalog toolCatalog,
    IAiToolSchemaValidator toolSchemaValidator,
    IOptions<WhatsAppFlowOptions> flowOptions) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<TenantWhatsAppSettingsDto>>> Get(CancellationToken ct)
    {
        var channel = await db.WhatsAppChannelSettings.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == 1, ct);
        var ai = await db.TenantAiSettings.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == 1, ct);
        var sessions = await db.WhatsAppCommerceSessions.AsNoTracking()
            .Where(x => x.TenantId == 1)
            .OrderByDescending(x => x.UpdatedAt)
            .Take(20)
            .ToListAsync(ct);
        return Ok(ApiResponse<TenantWhatsAppSettingsDto>.SuccessResponse(ToDto(channel, ai, sessions.Select(ToFlowSession).ToArray())));
    }

    [HttpPut("channel")]
    public async Task<ActionResult<ApiResponse<TenantWhatsAppChannelDto>>> UpdateChannel(
        [FromBody] UpdateTenantWhatsAppChannelDto dto,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(dto.AppSecret) && !WhatsAppWebhookSignature.IsValidAppSecret(dto.AppSecret))
            return BadRequest(ApiResponse<TenantWhatsAppChannelDto>.ErrorResponse(WhatsAppWebhookSignature.InvalidAppSecretMessage));

        var channel = await db.WhatsAppChannelSettings.FirstOrDefaultAsync(x => x.TenantId == 1, ct);
        if (channel is null)
        {
            if (string.IsNullOrWhiteSpace(dto.AccessToken))
                return BadRequest(ApiResponse<TenantWhatsAppChannelDto>.ErrorResponse("El Access Token es obligatorio al crear el canal."));
            channel = new WhatsAppChannelSetting { TenantId = 1 };
            db.WhatsAppChannelSettings.Add(channel);
        }

        var criticalChange = channel.PhoneNumberId != dto.PhoneNumberId.Trim()
            || channel.BusinessAccountId != dto.BusinessAccountId.Trim()
            || !string.IsNullOrWhiteSpace(dto.AccessToken);
        channel.PhoneNumberId = dto.PhoneNumberId.Trim();
        channel.BusinessAccountId = dto.BusinessAccountId.Trim();
        channel.DisplayPhoneNumber = dto.DisplayPhoneNumber.Trim();
        channel.WebhookVerifyToken = dto.WebhookVerifyToken.Trim();
        if (!string.IsNullOrWhiteSpace(dto.AppSecret)) channel.AppSecret = dto.AppSecret.Trim();
        channel.FlowId = Clean(dto.FlowId);
        channel.IsActive = dto.IsActive;
        if (!string.IsNullOrWhiteSpace(dto.AccessToken)) channel.AccessToken = dto.AccessToken.Trim();
        if (criticalChange)
        {
            channel.IsVerified = false;
            channel.LastVerifiedAt = null;
            channel.FlowEnabled = false;
        }
        if (dto.FlowEnabled && (!channel.IsActive || !channel.IsVerified || string.IsNullOrWhiteSpace(channel.FlowId)
            || string.IsNullOrWhiteSpace(flowOptions.Value.PrivateKey) || !WhatsAppWebhookSignature.IsValidAppSecret(channel.AppSecret)
            || !flowOptions.Value.Enabled))
            return Conflict(ApiResponse<TenantWhatsAppChannelDto>.ErrorResponse("Verifica el canal, registra el Flow ID y habilita WhatsAppFlow en el entorno antes de activarlo."));
        channel.FlowEnabled = dto.FlowEnabled;
        await db.SaveChangesAsync(ct);
        return Ok(ApiResponse<TenantWhatsAppChannelDto>.SuccessResponse(ToChannelDto(channel)));
    }

    [HttpPost("channel/test-connection")]
    public async Task<ActionResult<ApiResponse<TenantWhatsAppChannelDto>>> TestChannel(CancellationToken ct)
    {
        var channel = await db.WhatsAppChannelSettings.FirstOrDefaultAsync(x => x.TenantId == 1, ct);
        if (channel is null) return NotFound(ApiResponse<TenantWhatsAppChannelDto>.ErrorResponse("Canal no configurado."));
        var result = await cloud.TestConnectionAsync(channel.PhoneNumberId, channel.AccessToken, ct);
        channel.IsVerified = result.Success;
        channel.LastVerifiedAt = result.Success ? clock.UtcNow : null;
        if (result.Success && !string.IsNullOrWhiteSpace(result.DisplayPhoneNumber)) channel.DisplayPhoneNumber = result.DisplayPhoneNumber;
        if (!result.Success) channel.FlowEnabled = false;
        await db.SaveChangesAsync(ct);
        return result.Success
            ? Ok(ApiResponse<TenantWhatsAppChannelDto>.SuccessResponse(ToChannelDto(channel), "Conexión central verificada."))
            : BadRequest(ApiResponse<TenantWhatsAppChannelDto>.ErrorResponse(result.ErrorMessage ?? "Meta rechazó la conexión."));
    }

    [HttpPut("ai")]
    public async Task<ActionResult<ApiResponse<TenantAiSettingDto>>> UpdateAi(
        [FromBody] UpdateTenantAiSettingDto dto,
        CancellationToken ct)
    {
        var provider = dto.Provider.Trim().ToLowerInvariant();
        if (provider is not ("openai" or "gemini"))
            return BadRequest(ApiResponse<TenantAiSettingDto>.ErrorResponse("El proveedor debe ser openai o gemini."));
        var ai = await db.TenantAiSettings.FirstOrDefaultAsync(x => x.TenantId == 1, ct);
        if (ai is null)
        {
            ai = new TenantAiSetting { TenantId = 1 };
            db.TenantAiSettings.Add(ai);
        }
        var criticalChange = ai.Provider != provider || ai.Model != dto.Model.Trim();
        ai.Provider = provider;
        ai.Model = dto.Model.Trim();
        ai.IsActive = dto.IsActive;
        ai.Temperature = dto.Temperature;
        ai.MaxContextMessages = Math.Clamp(dto.MaxContextMessages, 1, 100);
        ai.AssistantName = dto.AssistantName.Trim();
        ai.PromptObjective = Clean(dto.PromptObjective);
        ai.PromptPersonality = Clean(dto.PromptPersonality);
        ai.PromptRequiredRules = Clean(dto.PromptRequiredRules);
        ai.PromptFixedBranchInfo = Clean(dto.PromptFixedBranchInfo);
        ai.PromptAdditionalInstructions = Clean(dto.PromptAdditionalInstructions);
        ai.TransferMessage = string.IsNullOrWhiteSpace(dto.TransferMessage) ? "Un asesor continuará con tu atención." : dto.TransferMessage.Trim();
        if (criticalChange)
        {
            ai.IsVerified = false;
            ai.LastTestedAt = null;
        }
        await db.SaveChangesAsync(ct);
        return Ok(ApiResponse<TenantAiSettingDto>.SuccessResponse(ToAiDto(ai)));
    }

    [HttpPost("ai/test-connection")]
    public async Task<ActionResult<ApiResponse<TenantAiSettingDto>>> TestAi(CancellationToken ct)
    {
        var ai = await db.TenantAiSettings.FirstOrDefaultAsync(x => x.TenantId == 1, ct);
        if (ai is null) return NotFound(ApiResponse<TenantAiSettingDto>.ErrorResponse("IA central no configurada."));
        ai.LastTestedAt = clock.UtcNow;
        var apiKey = aiApiKeys.GetApiKey(ai.Provider);
        if (string.IsNullOrWhiteSpace(apiKey))
            return await RejectAiTest(ai, $"Falta la variable de entorno {aiApiKeys.GetEnvironmentVariableName(ai.Provider)}.", ct);
        var models = await aiProviders.ListModelsAsync(ai.Provider, apiKey, ct);
        if (!models.Success)
            return await RejectAiTest(ai, models.ErrorMessage ?? "No se pudo validar el proveedor de IA.", ct);
        if (!models.Models.Any(x => string.Equals(x.Id, ai.Model, StringComparison.OrdinalIgnoreCase)))
            return await RejectAiTest(ai, "El modelo configurado no está disponible para esta API Key.", ct);
        var provider = aiChatProviders.Resolve(ai.Provider);
        if (provider is null)
            return await RejectAiTest(ai, "El proveedor no soporta generación de conversación.", ct);
        var schemaError = toolSchemaValidator.Validate(toolCatalog.All);
        if (schemaError is not null)
            return await RejectAiTest(ai, schemaError.ToString(), ct);
        var generation = await provider.GenerateAsync(new AiChatRequest(
            ai.Model,
            apiKey,
            [new("user", "Responde únicamente OK y no llames herramientas.")],
            toolCatalog.All,
            ai.Temperature), ct);
        if (generation.Error is not null || string.IsNullOrWhiteSpace(generation.Text) && generation.ToolCalls.Count == 0)
            return await RejectAiTest(ai, generation.Error ?? "El modelo no produjo una respuesta compatible.", ct);
        ai.IsVerified = true;
        await db.SaveChangesAsync(ct);
        return Ok(ApiResponse<TenantAiSettingDto>.SuccessResponse(ToAiDto(ai), "Conexión de IA central verificada."));
    }

    private async Task<ActionResult<ApiResponse<TenantAiSettingDto>>> RejectAiTest(TenantAiSetting ai, string message, CancellationToken ct)
    {
        ai.IsVerified = false;
        await db.SaveChangesAsync(ct);
        return BadRequest(ApiResponse<TenantAiSettingDto>.ErrorResponse(message));
    }

    private TenantWhatsAppSettingsDto ToDto(
        WhatsAppChannelSetting? channel,
        TenantAiSetting? ai,
        IReadOnlyCollection<WhatsAppFlowSessionDto> sessions) => new(
        channel is null ? null : ToChannelDto(channel), ai is null ? null : ToAiDto(ai),
        channel is null ? null : $"{Request.Scheme}://{Request.Host}/api/whatsapp/flows/{channel.PublicId}/data-exchange",
        flowOptions.Value.Enabled,
        !string.IsNullOrWhiteSpace(flowOptions.Value.PrivateKey),
        sessions);
    private static TenantWhatsAppChannelDto ToChannelDto(WhatsAppChannelSetting x) => new(
        x.PublicId, x.PhoneNumberId, x.BusinessAccountId, x.DisplayPhoneNumber, !string.IsNullOrWhiteSpace(x.AccessToken),
        x.WebhookVerifyToken, WhatsAppWebhookSignature.IsValidAppSecret(x.AppSecret), x.FlowId, x.IsActive, x.IsVerified, x.FlowEnabled, x.LastVerifiedAt);
    private static TenantAiSettingDto ToAiDto(TenantAiSetting x) => new(
        x.Provider, x.Model, x.IsActive, x.IsVerified, x.Temperature, x.MaxContextMessages, x.AssistantName,
        x.PromptObjective, x.PromptPersonality, x.PromptRequiredRules, x.PromptFixedBranchInfo, x.PromptAdditionalInstructions, x.TransferMessage);
    private static WhatsAppFlowSessionDto ToFlowSession(WhatsAppCommerceSession session)
    {
        try
        {
            using var document = JsonDocument.Parse(session.StateJson);
            var root = document.RootElement;
            var schemaVersion = root.TryGetProperty("schemaVersion", out var schema) && schema.TryGetInt32(out var parsed) ? parsed : 1;
            var screen = root.TryGetProperty("lastScreen", out var lastScreen) ? lastScreen.GetString() ?? "UNKNOWN" : "UNKNOWN";
            var category = root.TryGetProperty("category", out var selectedCategory) && selectedCategory.ValueKind == JsonValueKind.String
                ? selectedCategory.GetString()
                : null;
            return new(session.CorrelationId, $"v{schemaVersion}", session.Status, screen, category, session.UpdatedAt, session.ExpiresAt);
        }
        catch (JsonException)
        {
            return new(session.CorrelationId, "unknown", session.Status, "RECOVERY", null, session.UpdatedAt, session.ExpiresAt);
        }
    }
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record TenantWhatsAppSettingsDto(
    TenantWhatsAppChannelDto? Channel,
    TenantAiSettingDto? Ai,
    string? DataExchangeUrl,
    bool FlowEnvironmentEnabled,
    bool PrivateKeyConfigured,
    IReadOnlyCollection<WhatsAppFlowSessionDto> FlowSessions);
public sealed record WhatsAppFlowSessionDto(Guid CorrelationId, string FlowVersion, string Status, string Screen, string? Category, DateTime UpdatedAt, DateTime ExpiresAt);
public sealed record TenantWhatsAppChannelDto(Guid PublicId, string PhoneNumberId, string BusinessAccountId, string DisplayPhoneNumber, bool AccessTokenConfigured, string WebhookVerifyToken, bool AppSecretConfigured, string? FlowId, bool IsActive, bool IsVerified, bool FlowEnabled, DateTime? LastVerifiedAt);
public sealed record TenantAiSettingDto(string Provider, string Model, bool IsActive, bool IsVerified, double? Temperature, int MaxContextMessages, string AssistantName, string? PromptObjective, string? PromptPersonality, string? PromptRequiredRules, string? PromptFixedBranchInfo, string? PromptAdditionalInstructions, string TransferMessage);

public sealed class UpdateTenantWhatsAppChannelDto
{
    [Required, StringLength(64)] public string PhoneNumberId { get; set; } = string.Empty;
    [Required, StringLength(64)] public string BusinessAccountId { get; set; } = string.Empty;
    [Required, StringLength(32)] public string DisplayPhoneNumber { get; set; } = string.Empty;
    [StringLength(1000)] public string? AccessToken { get; set; }
    [Required, StringLength(200)] public string WebhookVerifyToken { get; set; } = string.Empty;
    [StringLength(255)] public string? AppSecret { get; set; }
    [StringLength(64)] public string? FlowId { get; set; }
    public bool IsActive { get; set; }
    public bool FlowEnabled { get; set; }
}

public sealed class UpdateTenantAiSettingDto
{
    [Required, StringLength(40)] public string Provider { get; set; } = string.Empty;
    [Required, StringLength(120)] public string Model { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    [Range(0, 2)] public double? Temperature { get; set; }
    [Range(1, 100)] public int MaxContextMessages { get; set; } = 20;
    [StringLength(200)] public string AssistantName { get; set; } = string.Empty;
    [StringLength(4000)] public string? PromptObjective { get; set; }
    [StringLength(2000)] public string? PromptPersonality { get; set; }
    [StringLength(8000)] public string? PromptRequiredRules { get; set; }
    [StringLength(8000)] public string? PromptFixedBranchInfo { get; set; }
    [StringLength(8000)] public string? PromptAdditionalInstructions { get; set; }
    [StringLength(1000)] public string TransferMessage { get; set; } = string.Empty;
}
