using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Services;

public sealed class WhatsAppManualWorkQueue(ApplicationDbContext db, ICurrentUser user, IBranchContext branches,
    ICurrentTenant tenant, IIntegrationSecretProtector protector, IClock clock, WhatsAppManualOperations operations,
    IFirebaseGcsStorage storage, IHttpContextAccessor http,
    ILogger<WhatsAppManualWorkQueue> logger, WhatsAppProcessingStore processingStore)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public sealed record Command(int ActorId, int AssignedBranchId, int WorkBranchId, string Operation,
        int? ConversationId, JsonElement Body, string Scheme, string Host, string PathBase, DateTime? AttentionVersion);
    public sealed record Result(int StatusCode, JsonElement Body);

    public async Task<ActionResult<ApiResponse<T>>> EnqueueAsync<T>(string operation, int? conversationId,
        JsonElement body, HttpRequest request, CancellationToken ct)
    {
        if (!await CanUseAsync(conversationId, ct)) return new ForbidResult();
        if (Encoding.UTF8.GetByteCount(body.GetRawText()) > 32768)
            return new ObjectResult(ApiResponse<T>.ErrorResponse("La operación supera el tamaño permitido.")) { StatusCode = 413 };
        var key = RequestKey(request);
        var command = new Command(user.Id, user.BranchId, branches.EffectiveBranchId ?? user.BranchId, operation, conversationId,
            body, request.Scheme, request.Host.Value ?? "localhost", request.PathBase.Value ?? "",
            conversationId.HasValue ? await db.WhatsAppConversations.Where(x => x.Id == conversationId).Select(x => (DateTime?)x.AttentionModeUpdatedAt).SingleAsync(ct) : null);
        var payload = JsonSerializer.Serialize(command, JsonOptions);
        var deduplication = Hash($"manual:{user.Id}:{key}");
        var fingerprintBody = operation == "send-media" && body.TryGetProperty("mediaFingerprint", out var mediaHash)
            ? JsonSerializer.SerializeToElement(new { mediaFingerprint = mediaHash.GetString() }) : body;
        var fingerprint = Hash(JsonSerializer.Serialize(new { operation, conversationId, body = fingerprintBody }, JsonOptions));
        var protectedPayload = protector.Protect(payload);
        var metadata = JsonSerializer.Serialize(new { fingerprint, operation,
            mediaFingerprint = body.TryGetProperty("mediaFingerprint", out var media) ? media.GetString() : null });
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var identity = conversationId.HasValue ? await db.WhatsAppConversations.AsNoTracking()
            .Where(x => x.Id == conversationId).Select(x => new
            {
                x.PhoneNumber, x.WhatsAppUserId,
                PhoneNumberId = x.ChannelSettingId.HasValue
                    ? db.WhatsAppChannelSettings.Where(c => c.Id == x.ChannelSettingId).Select(c => c.PhoneNumberId).FirstOrDefault()
                    : db.WhatsAppBranchSettings.Where(c => c.BranchId == x.BranchId).Select(c => c.PhoneNumberId).FirstOrDefault()
            }).SingleAsync(ct) : null;
        var aliases = identity?.PhoneNumberId is string phoneId
            ? WhatsAppWebhookEnvelope.AliasesFor(phoneId, identity.PhoneNumber, identity.WhatsAppUserId) : [];
        var resolved = await processingStore.ResolvePartitionAsync(tenant.TenantId, identity?.PhoneNumberId, aliases,
            conversationId, aliases.FirstOrDefault() ?? Hash($"manual:{conversationId?.ToString() ?? $"actor:{user.Id}"}"), ct);
        var partition = resolved.Key;
        aliases = resolved.Aliases;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO whatsapp_webhook_event
                (tenant_id, event_type, raw_payload, processed, processing_state, deduplication_key,
                 partition_key, partition_aliases, protected_payload, requested_by_user_id, conversation_id,
                 next_attempt_at, created_at, updated_at)
            VALUES ({tenant.TenantId}, 'manual_operation', {metadata}::jsonb, false, 'pending', {deduplication},
                {partition}, {aliases}, {protectedPayload}, {user.Id}, {conversationId}, {clock.UtcNow}, {clock.UtcNow}, {clock.UtcNow})
            ON CONFLICT (tenant_id, deduplication_key) WHERE deduplication_key IS NOT NULL DO NOTHING
            """, ct);
        var item = await db.WhatsAppWebhookEvents.AsNoTracking().SingleAsync(x => x.DeduplicationKey == deduplication, ct);
        using var raw = JsonDocument.Parse(item.RawPayload);
        if (!raw.RootElement.TryGetProperty("fingerprint", out var previous) || previous.GetString() != fingerprint)
            return new ConflictObjectResult(ApiResponse<T>.ErrorResponse("La clave de envío ya pertenece a otra solicitud o su resultado expiró."));
        await transaction.CommitAsync(ct);
        await transaction.DisposeAsync();
        await WhatsAppDatabaseSignal.NotifyAsync(db.Database, logger, ct);
        return new ObjectResult(Pending(item.Id)) { StatusCode = StatusCodes.Status202Accepted };
    }

    public async Task<ActionResult<ApiResponse<WhatsAppMessageDto>>> EnqueueMediaAsync(int conversationId,
        IFormFile? file, string? caption, HttpRequest request, CancellationToken ct)
    {
        if (file is null || file.Length is <= 0 or > 69_000_000)
            return new BadRequestObjectResult(ApiResponse<WhatsAppMessageDto>.ErrorResponse("Seleccione un archivo de hasta 69 MB."));
        if (caption?.Trim().Length > 4096)
            return new BadRequestObjectResult(ApiResponse<WhatsAppMessageDto>.ErrorResponse("El texto no puede superar 4096 caracteres."));
        if (!await CanUseAsync(conversationId, ct)) return new ForbidResult();
        var key = RequestKey(request);
        request.Headers["X-WhatsApp-Request-Id"] = key;
        var deduplication = Hash($"manual:{user.Id}:{key}");
        var fileName = WhatsAppWebhookProcessor.SanitizeFileName(file.FileName);
        await using var content = new MemoryStream();
        await file.CopyToAsync(content, ct);
        var bytes = content.ToArray();
        var mediaFingerprint = Hash(JsonSerializer.Serialize(new { conversationId, caption, fileName, file.ContentType,
            contentHash = Convert.ToHexString(SHA256.HashData(bytes)) }));
        var existing = await db.WhatsAppWebhookEvents.AsNoTracking().FirstOrDefaultAsync(x => x.DeduplicationKey == deduplication, ct);
        if (existing is not null)
        {
            using var metadata = JsonDocument.Parse(existing.RawPayload);
            if (existing.ConversationId != conversationId || !metadata.RootElement.TryGetProperty("mediaFingerprint", out var previous)
                || previous.GetString() != mediaFingerprint)
                return new ConflictObjectResult(ApiResponse<WhatsAppMessageDto>.ErrorResponse("La clave de envío ya pertenece a otro archivo o solicitud."));
            return new ObjectResult(Pending(existing.Id)) { StatusCode = StatusCodes.Status202Accepted };
        }
        var objectName = $"whatsapp/{tenant.TenantId}/manual/{conversationId}/{key}/{Guid.NewGuid():N}/{fileName}";
        string url;
        try { url = await storage.UploadPublicObjectAsync(bytes, objectName, file.ContentType, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning("WhatsApp media staging failed. Category={Category}", ex.GetType().Name);
            return new BadRequestObjectResult(ApiResponse<WhatsAppMessageDto>.ErrorResponse("No se pudo guardar el archivo en Firebase Storage."));
        }
        return await EnqueueAsync<WhatsAppMessageDto>("send-media", conversationId,
            JsonSerializer.SerializeToElement(new { caption, fileName, file.ContentType, objectName, url, mediaFingerprint }), request, ct);
    }

    public async Task<IActionResult> GetResultAsync(int id, CancellationToken ct)
    {
        var item = await db.WhatsAppWebhookEvents.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.EventType == "manual_operation" && x.RequestedByUserId == user.Id, ct);
        if (item is null || !await CanUseAsync(item.ConversationId, ct)) return new NotFoundResult();
        if (item.ProcessingState == "review")
            return new ConflictObjectResult(ApiResponse<object>.ErrorResponse($"El envío {id} requiere revisión: el resultado puede ser incierto. Revisa el chat antes de enviar otra vez."));
        if (item.ProcessingState != "completed")
            return new ObjectResult(Pending(id)) { StatusCode = StatusCodes.Status202Accepted };
        if (item.ProtectedResult is null)
            return new ObjectResult(ApiResponse<object>.ErrorResponse("El resultado expiró. Consulta el historial del chat.")) { StatusCode = StatusCodes.Status410Gone };
        var result = JsonSerializer.Deserialize<Result>(protector.Unprotect(item.ProtectedResult), JsonOptions)!;
        return new ObjectResult(result.Body) { StatusCode = result.StatusCode };
    }

    private async Task<bool> CanUseAsync(int? conversationId, CancellationToken ct)
    {
        if (!tenant.HasTenant || user.Id <= 0 || !user.IsAuthenticated
            || !Roles.IsSuperadmin(user.Role) && !Roles.IsAdminOrCashier(user.Role)) return false;
        var actor = await db.Users.AsNoTracking().Where(x => x.Id == user.Id && x.Active && x.WebAccessEnabled)
            .Select(x => new { x.BranchId, x.Role }).FirstOrDefaultAsync(ct);
        if (actor is null || actor.BranchId != user.BranchId || actor.Role is not (UserRole.Superadmin or UserRole.Admin or UserRole.Cashier)
            || !string.Equals(actor.Role.Value.ToString(), user.Role, StringComparison.OrdinalIgnoreCase)) return false;
        if (!conversationId.HasValue) return true;
        var conversation = await db.WhatsAppConversations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == conversationId, ct);
        return conversation is not null && await operations.CanAccessConversationAsync(conversation, ct);
    }

    public async Task ExecuteAsync(WhatsAppWebhookEvent job, string payload, CancellationToken ct)
    {
        var command = JsonSerializer.Deserialize<Command>(payload, JsonOptions)!;
        if (command.ActorId != job.RequestedByUserId || command.ConversationId != job.ConversationId)
            throw new InvalidOperationException("Invalid persisted manual operation context.");
        var actor = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == command.ActorId, ct);
        Result result;
        if (actor is null || !actor.Active || !actor.WebAccessEnabled
            || actor.Role is not (UserRole.Superadmin or UserRole.Admin or UserRole.Cashier)
            || actor.BranchId != command.AssignedBranchId
            || actor.Role != UserRole.Superadmin && actor.BranchId != command.WorkBranchId
            || !await db.Branches.AnyAsync(x => x.Id == command.WorkBranchId, ct))
            result = Error(403, "El usuario ya no está autorizado para completar esta operación.");
        else
        {
            var previous = http.HttpContext;
            var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, actor.Id.ToString()), new Claim(ClaimTypes.Role, actor.Role.Value.ToString()),
                new Claim("branch_id", actor.BranchId.ToString()), new Claim("tenant_id", tenant.TenantId.ToString())
            }, "WhatsAppManualWorker")) };
            context.Request.Headers["X-Branch-Id"] = command.WorkBranchId.ToString();
            context.Request.Scheme = command.Scheme;
            context.Request.Host = new HostString(command.Host);
            context.Request.PathBase = new PathString(command.PathBase);
            try
            {
                http.HttpContext = context;
                operations.ControllerContext = new ControllerContext { HttpContext = context };
                var currentVersion = command.ConversationId.HasValue
                    ? await db.WhatsAppConversations.AsNoTracking().Where(x => x.Id == command.ConversationId)
                        .Select(x => (DateTime?)x.AttentionModeUpdatedAt).FirstOrDefaultAsync(ct) : null;
                if (!await CanUseAsync(command.ConversationId, ct) || currentVersion != command.AttentionVersion)
                    result = Error(409, "La atención cambió antes de ejecutar el envío. Revisa la conversación.");
                else result = command.Operation switch
                {
                    "send-message" => Capture(await operations.SendMessage(command.ConversationId!.Value, command.Body.Deserialize<SendWhatsAppMessageDto>(JsonOptions)!, ct)),
                    "send-product" => Capture(await operations.SendProductDetails(command.ConversationId!.Value, command.Body.GetProperty("productId").GetInt32(), ct)),
                    "send-menu" => Capture(await operations.SendMenu(command.ConversationId!.Value, ct)),
                    "send-quick-reply" => Capture(await operations.SendQuickReply(command.ConversationId!.Value, command.Body.Deserialize<SendWhatsAppQuickReplyDto>(JsonOptions)!, ct)),
                    "send-template" => Capture(await operations.SendTemplate(command.Body.Deserialize<SendWhatsAppTemplateDto>(JsonOptions)!, ct)),
                    "sync-templates" => Capture(await operations.SyncTemplates(command.Body.Deserialize<SyncWhatsAppTemplatesDto>(JsonOptions), ct)),
                    "send-media" => await SendMediaAsync(command, ct),
                    _ => Error(400, "Operación desconocida.")
                };
            }
            finally { http.HttpContext = previous; }
        }
        var encrypted = protector.Protect(JsonSerializer.Serialize(result, JsonOptions));
        var changed = await db.WhatsAppWebhookEvents.Where(x => x.Id == job.Id && x.LeaseToken == job.LeaseToken
            && x.ProcessingState == "processing" && x.LeaseUntil > clock.UtcNow)
            .ExecuteUpdateAsync(x => x.SetProperty(e => e.ProtectedResult, encrypted).SetProperty(e => e.Processed, true), ct);
        if (changed != 1) throw new InvalidOperationException("Manual operation lease expired before result persistence.");
    }

    private async Task<Result> SendMediaAsync(Command command, CancellationToken ct)
    {
        var body = command.Body;
        var name = body.GetProperty("objectName").GetString()!;
        if (!name.StartsWith($"whatsapp/{tenant.TenantId}/manual/{command.ConversationId}/", StringComparison.Ordinal))
            return Error(403, "Archivo fuera del contexto de la conversación.");
        var bytes = await storage.DownloadObjectAsync(name, 69_000_000, ct);
        await using var stream = new MemoryStream(bytes, writable: false);
        var file = new FormFile(stream, 0, stream.Length, "file", body.GetProperty("fileName").GetString()!)
        {
            Headers = new HeaderDictionary(), ContentType = body.GetProperty("ContentType").GetString() ?? "application/octet-stream"
        };
        var caption = body.TryGetProperty("caption", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        return Capture(await operations.SendMediaMessage(command.ConversationId!.Value, file, caption, ct, body.GetProperty("url").GetString()));
    }

    private static Result Capture<T>(ActionResult<ApiResponse<T>> action) => action.Result switch
    {
        ObjectResult result => new(result.StatusCode ?? 200, JsonSerializer.SerializeToElement(result.Value, JsonOptions)),
        ForbidResult => Error(403, "No tienes acceso a esta conversación."),
        StatusCodeResult result => Error(result.StatusCode, "No se pudo completar la operación."),
        _ => new(200, JsonSerializer.SerializeToElement(action.Value, JsonOptions))
    };

    private static Result Error(int status, string message) => new(status, JsonSerializer.SerializeToElement(ApiResponse<object>.ErrorResponse(message), JsonOptions));
    private static object Pending(int id) => ApiResponse<object>.SuccessResponse(new { operationId = id, pending = true }, "Operación aceptada para procesamiento.");
    private static string RequestKey(HttpRequest request)
    {
        var value = request.Headers["X-WhatsApp-Request-Id"].FirstOrDefault();
        return Guid.TryParse(value, out var id) ? id.ToString("N") : Guid.NewGuid().ToString("N");
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
