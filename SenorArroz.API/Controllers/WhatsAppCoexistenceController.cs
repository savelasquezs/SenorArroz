using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SenorArroz.API.Hosting;
using SenorArroz.API.Services;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Authorize(Roles = "Superadmin, Admin")]
[Route("api/branches/{branchId:int}/whatsapp-coexistence")]
public sealed class WhatsAppCoexistenceController(IApplicationDbContext db, ICurrentTenant tenant, ICurrentUser user,
    IClock clock, IIntegrationSecretProtector protector, WhatsAppCoexistenceClient meta, WhatsAppCoexistencePilot? pilot = null) : ControllerBase
{
    private const string SantanderPhone = "+57 301 227 4655";
    private long NowUnix => new DateTimeOffset(clock.UtcNow).ToUnixTimeSeconds();

    public sealed record PilotCallbackRequest(bool Active);
    public sealed record ConnectRequest(string Session, string Code, string WabaId, string? PhoneNumberId, string? DisplayPhoneNumber = null);

    [HttpGet]
    public async Task<IActionResult> Get(int branchId, CancellationToken ct)
    {
        if (!await AllowedAsync(branchId, ct)) return Forbid();
        var setting = await db.WhatsAppBranchSettings.AsNoTracking().SingleOrDefaultAsync(x => x.BranchId == branchId && x.CoexistenceEnabled, ct);
        var recoveryUnavailable = IsRecoveryCredentialUnavailable(setting);
        var state = setting?.CoexistenceEnabled != true ? "pending" : !setting.IsActive ? "disconnected"
            : (!setting.IsVerified || recoveryUnavailable) && setting.ConnectionState != "disconnected" ? "degraded" : setting.ConnectionState;
        var recoveryAvailable = meta.Configured
            && pilot is { PhoneNumberId: { } phoneId, RecoveryWabaId: { } wabaId, RecoveryAccessToken: not null }
            && NeedsRecovery(setting) && !HasDifferentCoexistenceLine(setting, phoneId, wabaId);
        return Ok(ApiResponse<object>.SuccessResponse(new { configured = meta.Configured,
            state,
            enabled = setting?.CoexistenceEnabled == true,
            error = recoveryUnavailable ? "La credencial de recuperación no está disponible. Recupera Santander con una credencial vigente." : setting?.ConnectionError,
            connectedAt = setting?.CoexistenceConnectedAt, historyStatus = setting?.HistorySyncStatus ?? "not_started",
            contactsStatus = setting?.ContactsSyncStatus ?? "not_started", lastSyncAt = setting?.LastSyncAt,
            historyRequestId = setting?.HistorySyncRequestId, contactsRequestId = setting?.ContactsSyncRequestId,
            displayPhoneNumber = pilot is null ? setting?.DisplayPhoneNumber : SantanderPhone,
            pilotPhoneNumberId = pilot is null ? null : setting?.CoexistenceEnabled == true ? setting.PhoneNumberId : null,
            pilotCallbackAvailable = pilot?.PhoneNumberId is not null && pilot.CallbackUrl is not null,
            pilotRecoveryAvailable = recoveryAvailable }));
    }

    [HttpPost("signup-session")]
    public async Task<IActionResult> Start(int branchId, CancellationToken ct)
    {
        if (!await AllowedAsync(branchId, ct)) return Forbid();
        if (!meta.Configured) return BadRequest(ApiResponse<object>.ErrorResponse("Configura la aplicación y el registro insertado de Meta en el servidor."));
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expires = clock.UtcNow.AddMinutes(15);
        db.WhatsAppWebhookEvents.Add(new WhatsAppWebhookEvent { TenantId = tenant.TenantId,
            EventType = "coexistence_signup", RequestedByUserId = user.Id, ProcessingState = "signup_pending",
            DeduplicationKey = Hash(nonce), RawPayload = JsonSerializer.Serialize(new { branchId, expires }),
            CreatedAt = clock.UtcNow, UpdatedAt = clock.UtcNow });
        await db.SaveChangesAsync(ct);
        return Ok(ApiResponse<object>.SuccessResponse(new { appId = meta.AppId, configurationId = meta.ConfigurationId,
            graphApiVersion = meta.Version, session = protector.Protect(JsonSerializer.Serialize(new Session(nonce, tenant.TenantId, branchId, user.Id, expires))) }));
    }

    [HttpPost("connect")]
    public async Task<IActionResult> Connect(int branchId, [FromBody] ConnectRequest request, CancellationToken ct)
    {
        if (!await AllowedAsync(branchId, ct)) return Forbid();
        Session? session;
        try { session = JsonSerializer.Deserialize<Session>(protector.Unprotect(request.Session)); }
        catch (Exception e) when (e is CryptographicException or JsonException or FormatException or ArgumentException)
        { return BadRequest(ApiResponse<object>.ErrorResponse("El registro no es válido. Inicia la conexión nuevamente.")); }
        if (session is null || session.TenantId != tenant.TenantId || session.UserId != user.Id
            || session.BranchId != branchId || session.ExpiresAt <= clock.UtcNow || request.Code.Length is < 1 or > 4096)
            return BadRequest(ApiResponse<object>.ErrorResponse("El registro expiró o pertenece a otro usuario."));
        if (pilot is not null && !WhatsAppCoexistencePilot.IsLine(request.DisplayPhoneNumber))
            return BadRequest(ApiResponse<object>.ErrorResponse("Para este piloto usa el número de Santander configurado en el servidor."));
        var key = Hash(session.Nonce);
        var claimed = await db.WhatsAppWebhookEvents.Where(x => x.DeduplicationKey == key && x.RequestedByUserId == user.Id
            && x.ProcessingState == "signup_pending").ExecuteUpdateAsync(x => x.SetProperty(e => e.ProcessingState, "signup_connecting"), ct);
        if (claimed != 1) return Conflict(ApiResponse<object>.ErrorResponse("Este registro ya fue utilizado. Consulta el diagnóstico de la línea."));
        var setting = await db.WhatsAppBranchSettings.SingleOrDefaultAsync(x => x.BranchId == branchId && x.CoexistenceEnabled, ct);
        try
        {
            await meta.VerifyWebhookFieldsAsync(ct, liveOnly: pilot is not null);
            var token = await meta.ExchangeAsync(request.Code, ct);
            await meta.VerifyPermissionsAsync(token, request.WabaId, ct);
            return await BindAsync(branchId, setting, request.WabaId, request.PhoneNumberId, token, request.DisplayPhoneNumber, ct);
        }
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            return Conflict(ApiResponse<object>.ErrorResponse("Esta línea ya está configurada. Conserva un único canal por número."));
        }
        catch (Exception e) when (e is BusinessException or HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            if (setting is not null)
            {
                setting.ConnectionState = "degraded";
                setting.ConnectionError = e is BusinessException ? e.Message : "La conexión con Meta no se confirmó. Revisa el estado antes de repetir el registro.";
                await db.SaveChangesAsync(CancellationToken.None);
            }
            return BadRequest(ApiResponse<object>.ErrorResponse(e is BusinessException ? e.Message : "No se pudo confirmar la conexión con Meta. Consulta el diagnóstico."));
        }
    }

    [HttpPost("pilot-recover")]
    public async Task<IActionResult> RecoverPilot(int branchId, CancellationToken ct)
    {
        if (pilot is not { PhoneNumberId: { } phoneId, RecoveryWabaId: { } wabaId, RecoveryAccessToken: { } token } || !meta.Configured)
            return NotFound();
        if (!await AllowedAsync(branchId, ct)) return Forbid();

        var setting = await db.WhatsAppBranchSettings.SingleOrDefaultAsync(x => x.BranchId == branchId && x.CoexistenceEnabled, ct);
        if (HasDifferentCoexistenceLine(setting, phoneId, wabaId))
            return Conflict(ApiResponse<object>.ErrorResponse("La sucursal ya tiene otra línea de coexistencia."));
        if (!NeedsRecovery(setting)) return await Get(branchId, ct);

        try
        {
            await meta.VerifyWebhookFieldsAsync(ct, liveOnly: true);
            long? expiresAt = null;
            if (pilot.TemporaryRecoveryToken)
                expiresAt = await meta.VerifyTemporaryPilotTokenAsync(token, wabaId, NowUnix, ct);
            else
                await meta.VerifyPilotRecoveryPermissionsAsync(token, wabaId, ct);
            return await BindAsync(branchId, setting, wabaId, phoneId, token, SantanderPhone, ct, recovery: true, temporaryExpiresAt: expiresAt);
        }
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            return Conflict(ApiResponse<object>.ErrorResponse("Esta línea ya está configurada. Conserva un único canal por número."));
        }
        catch (Exception e) when (e is BusinessException or HttpRequestException or TaskCanceledException
            or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return BadRequest(ApiResponse<object>.ErrorResponse(e is BusinessException ? e.Message
                : "No se pudo verificar la recuperación de Santander en Meta."));
        }
    }

    private bool NeedsRecovery(WhatsAppBranchSetting? setting) =>
        setting is not { CoexistenceEnabled: true, IsActive: true, IsVerified: true, ConnectionState: "connected" }
        || IsRecoveryCredentialUnavailable(setting);

    private bool IsRecoveryCredentialUnavailable(WhatsAppBranchSetting? setting) =>
        setting is { CoexistenceEnabled: true } && WhatsAppCoexistencePilot.IsRecoveryCredential(setting.AccessToken)
        && pilot?.UnprotectRecoveryCredential(setting.AccessToken, protector, NowUnix) is null;

    private static bool HasDifferentCoexistenceLine(WhatsAppBranchSetting? setting, string phoneId, string wabaId) =>
        setting?.CoexistenceEnabled == true && (setting.PhoneNumberId != phoneId || setting.BusinessAccountId != wabaId);

    private async Task<IActionResult> BindAsync(int branchId, WhatsAppBranchSetting? setting, string wabaId,
        string? requestedPhoneId, string token, string? expectedPhone, CancellationToken ct, bool recovery = false,
        long? temporaryExpiresAt = null)
    {
        var phone = await meta.PhoneAsync(wabaId, requestedPhoneId, token, ct, expectedPhone);
        var phoneId = phone.GetProperty("id").GetString()!;
        if (pilot?.PhoneNumberId is { } configuredPhoneId && configuredPhoneId != phoneId)
            return Conflict(ApiResponse<object>.ErrorResponse("Meta devolvió un identificador distinto al del piloto."));
        if (await db.WhatsAppChannelSettings.AnyAsync(x => x.PhoneNumberId == phoneId, ct)
            || await db.WhatsAppBranchSettings.AnyAsync(x => x.PhoneNumberId == phoneId && (x.BranchId != branchId || !x.CoexistenceEnabled), ct))
            return Conflict(ApiResponse<object>.ErrorResponse("Esta línea ya está configurada en otro canal."));
        if (setting is not null && setting.PhoneNumberId != phoneId
            && await db.WhatsAppConversations.AnyAsync(x => x.BranchLineId == setting.Id, ct))
            return Conflict(ApiResponse<object>.ErrorResponse("Esta sucursal ya tiene conversaciones de otro número. Conserva su canal antes de cambiar la línea."));

        if (recovery && pilot?.CallbackUrl is { } callback)
        {
            var current = await meta.ReadPhoneCallbackAsync(phoneId, token, ct);
            if (!string.IsNullOrWhiteSpace(current) && !string.Equals(current, callback, StringComparison.Ordinal))
                return Conflict(ApiResponse<object>.ErrorResponse("Santander tiene otro callback por número. Conserva su configuración antes de cambiarla."));
        }
        await meta.EnsureSubscriptionAsync(wabaId, token, ct);
        if (recovery) await meta.VerifySubscriptionAsync(wabaId, token, ct);

        var resetSync = setting is not null && (setting.ConnectionState == "disconnected" || setting.PhoneNumberId != phoneId);
        setting ??= new WhatsAppBranchSetting { TenantId = tenant.TenantId, BranchId = branchId };
        if (setting.Id == 0) db.WhatsAppBranchSettings.Add(setting);
        setting.PhoneNumberId = phoneId; setting.BusinessAccountId = wabaId;
        setting.DisplayPhoneNumber = phone.GetProperty("display_phone_number").GetString() ?? "";
        setting.AccessToken = recovery ? WhatsAppCoexistencePilot.ProtectRecoveryCredential(token, temporaryExpiresAt, protector) : token;
        setting.AppSecret = meta.AppSecret; setting.WebhookVerifyToken = meta.VerifyToken;
        setting.IsActive = setting.IsVerified = setting.CoexistenceEnabled = true;
        setting.LastVerifiedAt = setting.CoexistenceConnectedAt = clock.UtcNow;
        setting.ConnectionError = null; setting.ConnectionState = "syncing";
        if (resetSync)
        {
            setting.HistorySyncStatus = setting.ContactsSyncStatus = "not_started";
            setting.HistorySyncRequestId = setting.ContactsSyncRequestId = null;
        }
        if (pilot is not null)
        {
            setting.HistorySyncStatus = setting.ContactsSyncStatus = "skipped";
            setting.HistorySyncRequestId = setting.ContactsSyncRequestId = null;
            setting.ConnectionState = "connected";
        }
        await db.SaveChangesAsync(ct);
        if (pilot is null)
        {
            await StartSyncAsync(setting, "smb_app_state_sync", ct);
            await StartSyncAsync(setting, "history", ct);
        }
        return await Get(branchId, ct);
    }

    [HttpPost("check")]
    public async Task<IActionResult> Check(int branchId, CancellationToken ct)
    {
        if (!await AllowedAsync(branchId, ct)) return Forbid();
        var setting = await db.WhatsAppBranchSettings.SingleOrDefaultAsync(x => x.BranchId == branchId && x.CoexistenceEnabled, ct);
        if (setting is null) return NotFound();
        try
        {
            await meta.PhoneAsync(setting.BusinessAccountId, setting.PhoneNumberId, setting.AccessToken, ct);
            await meta.VerifySubscriptionAsync(setting.BusinessAccountId, setting.AccessToken, ct);
            setting.ConnectionError = null; setting.IsVerified = true; setting.LastVerifiedAt = clock.UtcNow;
            setting.ConnectionState = setting.HistorySyncStatus is "completed" or "declined" or "skipped" ? "connected" : "syncing";
        }
        catch (Exception e) when (e is BusinessException or HttpRequestException or TaskCanceledException or JsonException)
        {
            if (e is BusinessException) setting.IsVerified = false;
            setting.ConnectionState = "degraded";
            setting.ConnectionError = "No se pudo verificar coexistencia. Revisa permisos y conexión en Meta.";
        }
        await db.SaveChangesAsync(ct);
        return await Get(branchId, ct);
    }

    [HttpPost("pilot-callback")]
    public async Task<IActionResult> SetPilotCallback(int branchId, [FromBody] PilotCallbackRequest request, CancellationToken ct)
    {
        if (pilot?.PhoneNumberId is not { } phoneId || pilot.CallbackUrl is not { } callback) return NotFound();
        if (!await AllowedAsync(branchId, ct)) return Forbid();
        var setting = await db.WhatsAppBranchSettings.AsNoTracking().SingleOrDefaultAsync(
            x => x.BranchId == branchId && x.CoexistenceEnabled && x.PhoneNumberId == phoneId, ct);
        if (setting is null || !WhatsAppCoexistencePilot.IsLine(setting.DisplayPhoneNumber)) return NotFound();

        try
        {
            var current = await meta.ReadPhoneCallbackAsync(phoneId, setting.AccessToken, ct);
            if (!string.IsNullOrWhiteSpace(current) && !string.Equals(current, callback, StringComparison.Ordinal))
                return Conflict(ApiResponse<object>.ErrorResponse("Santander tiene otro callback por número. Conserva su configuración antes de cambiarla."));
            if (request.Active && (!setting.IsActive || !setting.IsVerified)) return Conflict();
            if (request.Active != (current == callback))
            {
                await meta.SetPhoneCallbackAsync(phoneId, setting.AccessToken, request.Active ? callback : null, ct);
                current = await meta.ReadPhoneCallbackAsync(phoneId, setting.AccessToken, ct);
                if (request.Active ? current != callback : !string.IsNullOrWhiteSpace(current))
                    return Conflict(ApiResponse<object>.ErrorResponse("El callback de Santander no pudo confirmarse en Meta."));
            }
            return Ok(ApiResponse<object>.SuccessResponse(new { active = request.Active }));
        }
        catch (Exception error) when (error is BusinessException or HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return BadRequest(ApiResponse<object>.ErrorResponse("Consulta el callback de Santander en Meta antes de repetir la operación."));
        }
    }

    private async Task StartSyncAsync(WhatsAppBranchSetting setting, string type, CancellationToken ct)
    {
        if ((type == "history" ? setting.HistorySyncStatus : setting.ContactsSyncStatus) != "not_started") return;
        if (type == "history") setting.HistorySyncStatus = "requesting"; else setting.ContactsSyncStatus = "requesting";
        await db.SaveChangesAsync(ct);
        var id = await meta.SyncAsync(setting.PhoneNumberId, setting.AccessToken, type, ct);
        if (type == "history") setting.HistorySyncRequestId = id;
        else setting.ContactsSyncRequestId = id;
        await db.SaveChangesAsync(ct);
        if (type == "history")
            await db.WhatsAppBranchSettings.Where(x => x.Id == setting.Id && x.HistorySyncStatus == "requesting")
                .ExecuteUpdateAsync(x => x.SetProperty(s => s.HistorySyncStatus, "requested"), ct);
        else
            await db.WhatsAppBranchSettings.Where(x => x.Id == setting.Id && x.ContactsSyncStatus == "requesting")
                .ExecuteUpdateAsync(x => x.SetProperty(s => s.ContactsSyncStatus, "requested"), ct);
    }

    private Task<bool> AllowedAsync(int branchId, CancellationToken ct) => tenant.HasTenant && user.Id > 0
        && (pilot is null || pilot.TenantId == tenant.TenantId && pilot.BranchId == branchId)
        && (Roles.IsSuperadmin(user.Role) || Roles.IsAdmin(user.Role) && user.BranchId == branchId)
        ? db.Branches.AnyAsync(x => x.Id == branchId && x.TenantId == tenant.TenantId, ct) : Task.FromResult(false);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private sealed record Session(string Nonce, int TenantId, int BranchId, int UserId, DateTime ExpiresAt);
}
