using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SenorArroz.API.Services;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Domain.Entities;

namespace SenorArroz.API.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/whatsapp/flows")]
public sealed class WhatsAppFlowsController(
    IApplicationDbContext db,
    IWhatsAppFlowCrypto crypto,
    WhatsAppCommerceFlowService commerce,
    IWhatsAppNotificationService notifications,
    ILogger<WhatsAppFlowsController> logger) : ControllerBase
{
    [HttpPost("{channelPublicId:guid}/data-exchange")]
    [RequestSizeLimit(256 * 1024)]
    [EnableRateLimiting("whatsapp-flow")]
    public async Task<IActionResult> DataExchange(
        Guid channelPublicId,
        [FromBody] WhatsAppEncryptedFlowRequest request,
        CancellationToken ct)
    {
        WhatsAppDecryptedFlowRequest decrypted;
        try
        {
            decrypted = crypto.Decrypt(request);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException)
        {
            logger.LogWarning("WhatsApp Flow rejected an invalid encrypted payload. ChannelPublicId={ChannelPublicId}", channelPublicId);
            return StatusCode(421);
        }

        JsonDocument document;
        try { document = JsonDocument.Parse(decrypted.Json, new JsonDocumentOptions { MaxDepth = 24 }); }
        catch (JsonException)
        {
            return Encrypted(WhatsAppCommerceFlowService.Recovery(1, "No pudimos leer la solicitud. Cierra este menú y escribe PEDIDO.", false), decrypted);
        }
        using var documentLifetime = document;
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return Encrypted(WhatsAppCommerceFlowService.Recovery(1, "No pudimos leer la solicitud. Cierra este menú y escribe PEDIDO.", false), decrypted);
        var action = GetString(root, "action") ?? string.Empty;
        var version = GetString(root, "version") ?? "3.0";
        var channel = await db.WhatsAppChannelSettings.AsNoTracking().FirstOrDefaultAsync(
            x => x.PublicId == channelPublicId && x.TenantId == 1, ct);
        if (channel is null)
            return Encrypted(WhatsAppCommerceFlowService.Recovery(1, "Este menú ya no está disponible. Cierra y escribe PEDIDO.", false), decrypted);
        if (action == "ping")
            return Encrypted(new Dictionary<string, object?> { ["version"] = version, ["data"] = new { status = "active" } }, decrypted);
        if (action is "client_error" or "data_exchange_error"
            || root.TryGetProperty("data", out var errorData) && errorData.ValueKind == JsonValueKind.Object && errorData.TryGetProperty("error", out _))
        {
            var errorCode = root.TryGetProperty("data", out var clientData)
                ? GetString(clientData, "error") ?? GetString(clientData, "error_code") ?? "unknown"
                : "unknown";
            logger.LogWarning("WhatsApp Flow client error received. ChannelPublicId={ChannelPublicId} Action={Action} ErrorCode={ErrorCode}",
                channelPublicId, action, errorCode.Length <= 80 ? errorCode : errorCode[..80]);
            return Encrypted(new Dictionary<string, object?> { ["version"] = version, ["data"] = new { acknowledged = true } }, decrypted);
        }
        if (action is not ("INIT" or "data_exchange" or "BACK"))
            return Encrypted(WhatsAppCommerceFlowService.Recovery(1, "No pudimos reconocer esa acción. Intenta nuevamente.", false), decrypted);

        if (!channel.IsActive || !channel.IsVerified || !channel.FlowEnabled)
            return Encrypted(WhatsAppCommerceFlowService.Recovery(1, "Los pedidos por este menú están pausados. Cierra y escribe ASESOR.", false), decrypted);

        var flowToken = GetString(root, "flow_token");
        var session = await commerce.FindSessionAsync(channel.Id, flowToken ?? string.Empty, ct);
        if (session is null)
        {
            if (string.Equals(GetString(root, "screen"), "RECOVERY", StringComparison.OrdinalIgnoreCase)
                && root.TryGetProperty("data", out var recoveryData))
                return Encrypted(WhatsAppCommerceFlowService.CompleteRecovery(flowToken ?? string.Empty, GetString(recoveryData, "command") ?? "restart"), decrypted);
            return Encrypted(WhatsAppCommerceFlowService.Recovery(1, "Esta sesión ya no está disponible. Cierra este menú y escribe PEDIDO.", false), decrypted);
        }

        var fingerprint = Fingerprint(root);
        var previous = await db.WhatsAppFlowExchanges.AsNoTracking().FirstOrDefaultAsync(
            x => x.SessionId == session.Id && x.RequestFingerprint == fingerprint, ct);
        if (previous is not null)
            return Content(crypto.Encrypt(WhatsAppFlowPayload.RestoreCompletionToken(previous.ResponseJson, flowToken!), decrypted.AesKey, decrypted.InitialVector), "text/plain", Encoding.UTF8);

        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
            : null;
        var screen = GetString(root, "screen");
        var previousBranchId = session.Conversation.OperationalBranchId;
        var previousMode = session.Conversation.AttentionMode;
        try
        {
            var data = root.TryGetProperty("data", out var dataElement) ? dataElement : default;
            var response = await commerce.HandleAsync(session, action, screen, flowToken!, data, ct);
            response["version"] = version;
            var responseJson = JsonSerializer.Serialize(response);
            db.WhatsAppFlowExchanges.Add(new WhatsAppFlowExchange
            {
                SessionId = session.Id,
                RequestFingerprint = fingerprint,
                ResponseJson = WhatsAppFlowPayload.WithoutTokens(responseJson)
            });
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
            if (previousBranchId != session.Conversation.OperationalBranchId || previousMode != session.Conversation.AttentionMode)
            {
                try
                {
                    await notifications.NotifyAttentionChangedAsync(session.Conversation.BranchId,
                        WhatsAppConversationMapper.ToDto(session.Conversation), ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning("WhatsApp Flow realtime update failed. ConversationId={ConversationId}", session.ConversationId);
                }
            }
            return Content(crypto.Encrypt(responseJson, decrypted.AesKey, decrypted.InitialVector), "text/plain", Encoding.UTF8);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (transaction is not null) await transaction.RollbackAsync(ct);
            logger.LogInformation("WhatsApp Flow concurrency recovered. CorrelationId={CorrelationId}", session.CorrelationId);
            return Encrypted(WhatsAppCommerceFlowService.Recovery(session.Version, "Tu pedido cambió en otra pantalla. Reintenta para cargar la versión más reciente.", true), decrypted);
        }
        catch (DbUpdateException)
        {
            if (transaction is not null) await transaction.RollbackAsync(ct);
            var stored = await db.WhatsAppFlowExchanges.AsNoTracking().FirstOrDefaultAsync(
                x => x.SessionId == session.Id && x.RequestFingerprint == fingerprint, ct);
            if (stored is not null)
                return Content(crypto.Encrypt(WhatsAppFlowPayload.RestoreCompletionToken(stored.ResponseJson, flowToken!), decrypted.AesKey, decrypted.InitialVector), "text/plain", Encoding.UTF8);
            throw;
        }
    }

    private ContentResult Encrypted(object response, WhatsAppDecryptedFlowRequest request) =>
        Content(crypto.Encrypt(JsonSerializer.Serialize(response), request.AesKey, request.InitialVector), "text/plain", Encoding.UTF8);

    private static string? GetString(JsonElement source, string name) =>
        source.ValueKind == JsonValueKind.Object && source.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Fingerprint(JsonElement value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) WriteCanonical(value, writer);
        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    private static void WriteCanonical(JsonElement value, Utf8JsonWriter writer)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(property.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) WriteCanonical(item, writer);
                writer.WriteEndArray();
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }
}
