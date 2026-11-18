using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.API.Services;

public sealed class WhatsAppCoexistenceClient(HttpClient http, IConfiguration configuration)
{
    private static readonly string[] RequiredScopes = ["whatsapp_business_management", "whatsapp_business_messaging"];

    public string AppId => configuration["WhatsAppCoexistence:AppId"] ?? "";
    public string ConfigurationId => configuration["WhatsAppCoexistence:ConfigurationId"] ?? "";
    public string AppSecret => configuration["WhatsAppCoexistence:AppSecret"] ?? "";
    public string VerifyToken => configuration["WhatsAppCoexistence:WebhookVerifyToken"] ?? "";
    public string Version => configuration["WhatsAppCoexistence:GraphApiVersion"] ?? "";
    public bool Configured => Regex.IsMatch(AppId, "^[0-9]+$") && Regex.IsMatch(ConfigurationId, "^[0-9]+$")
        && SenorArroz.Application.Common.Helpers.WhatsAppWebhookSignature.IsValidAppSecret(AppSecret)
        && VerifyToken.Length > 0 && Regex.IsMatch(Version, "^v[0-9]+\\.[0-9]+$");

    public async Task<string> ExchangeAsync(string code, CancellationToken ct)
    {
        var result = await RequestAsync(HttpMethod.Get, $"oauth/access_token?client_id={Id(AppId)}"
            + $"&client_secret={Uri.EscapeDataString(AppSecret)}&code={Uri.EscapeDataString(code)}", null, null, ct);
        return result.GetProperty("access_token").GetString() ?? throw new BusinessException("Meta no devolvió un token de acceso.");
    }

    public async Task VerifyPermissionsAsync(string token, string wabaId, CancellationToken ct)
    {
        var data = await ReadAuthorizedTokenAsync(token, ct);
        if (!HasRequiredAccountGrants(data, wabaId))
            throw new BusinessException("La cuenta seleccionada no está autorizada por este registro.");
    }

    internal async Task<long> VerifyTemporaryPilotTokenAsync(string token, string wabaId, long now, CancellationToken ct)
    {
        var data = await ReadAuthorizedTokenAsync(token, ct);
        if (!data.TryGetProperty("type", out var type) || !type.ValueEquals("USER")
            || !data.TryGetProperty("expires_at", out var expiration) || !expiration.TryGetInt64(out var expiresAt)
            || expiresAt <= now || expiresAt > now + 24 * 60 * 60)
            throw new BusinessException("La credencial temporal debe ser de usuario y vencer dentro de 24 horas.");

        if (!HasRequiredAccountGrants(data, wabaId) || data.GetProperty("scopes").EnumerateArray()
            .Any(scope => !scope.ValueEquals("public_profile") && !RequiredScopes.Contains(scope.GetString())))
            throw new BusinessException("La credencial temporal requiere únicamente permisos de WhatsApp autorizados para Santander.");

        return expiresAt;
    }

    private static bool HasRequiredAccountGrants(JsonElement data, string wabaId) =>
        data.TryGetProperty("granular_scopes", out var granular) && granular.ValueKind == JsonValueKind.Array
        && RequiredScopes.All(scope => granular.EnumerateArray().Any(grant => grant.TryGetProperty("scope", out var name)
            && name.GetString() == scope && grant.TryGetProperty("target_ids", out var targets) && targets.ValueKind == JsonValueKind.Array
            && targets.EnumerateArray().Any(target => target.GetString() == wabaId)));

    public async Task VerifyPilotRecoveryPermissionsAsync(string token, string wabaId, CancellationToken ct)
    {
        var data = await ReadAuthorizedTokenAsync(token, ct);
        if (!data.TryGetProperty("type", out var type) || !type.ValueEquals("SYSTEM_USER"))
            throw new BusinessException("La recuperación requiere un token de usuario del sistema.");

        if (data.TryGetProperty("granular_scopes", out var granular) && granular.ValueKind == JsonValueKind.Array
            && granular.EnumerateArray().Any(x => x.TryGetProperty("scope", out var scope) && RequiredScopes.Contains(scope.GetString())
                && x.TryGetProperty("target_ids", out var targets) && targets.ValueKind == JsonValueKind.Array
                && targets.EnumerateArray().Any(target => !target.ValueEquals(wabaId))))
            throw new BusinessException("La credencial de recuperación debe estar limitada a la cuenta de Santander.");
    }

    private async Task<JsonElement> ReadAuthorizedTokenAsync(string token, CancellationToken ct)
    {
        var result = await RequestAsync(HttpMethod.Get, $"debug_token?input_token={Uri.EscapeDataString(token)}",
            $"{AppId}|{AppSecret}", null, ct);
        var data = result.GetProperty("data");
        if (!data.TryGetProperty("is_valid", out var valid) || valid.ValueKind != JsonValueKind.True
            || data.GetProperty("app_id").GetString() != AppId)
            throw new BusinessException("El token no pertenece a la aplicación configurada o ya no es válido.");
        var scopes = data.GetProperty("scopes").EnumerateArray().Select(x => x.GetString()).ToHashSet();
        if (!RequiredScopes.All(scopes.Contains))
            throw new BusinessException("Faltan permisos de administración o mensajería de WhatsApp.");
        return data;
    }

    public async Task<JsonElement> PhoneAsync(string wabaId, string? phoneId, string token, CancellationToken ct, string? expectedNumber = null)
    {
        var result = await RequestAsync(HttpMethod.Get,
            $"{Id(wabaId)}/phone_numbers?fields=id,display_phone_number,is_on_biz_app,platform_type&limit=100", token, null, ct);
        var expected = SenorArroz.Application.Common.Services.WhatsAppWebhookIdentityReader.NormalizePhone(expectedNumber);
        var phones = result.GetProperty("data").EnumerateArray()
            .Where(x => (phoneId is null || x.GetProperty("id").GetString() == phoneId)
                && (expected is null || x.TryGetProperty("display_phone_number", out var number)
                    && SenorArroz.Application.Common.Services.WhatsAppWebhookIdentityReader.NormalizePhone(number.GetString()) == expected)).ToList();
        if (phones.Count != 1) throw new BusinessException("Selecciona un único número autorizado de la cuenta de WhatsApp.");
        var phone = phones[0];
        if (!phone.TryGetProperty("is_on_biz_app", out var app) || app.ValueKind != JsonValueKind.True
            || !phone.TryGetProperty("platform_type", out var platform) || platform.GetString() != "CLOUD_API")
            throw new BusinessException("Meta no confirma coexistencia entre la app Business y Cloud API para este número.");
        return phone.Clone();
    }

    public async Task EnsureSubscriptionAsync(string wabaId, string token, CancellationToken ct)
    {
        if (await IsSubscribedAsync(wabaId, token, ct)) return;
        var result = await RequestAsync(HttpMethod.Post, $"{Id(wabaId)}/subscribed_apps", token, JsonContent.Create(new { }), ct);
        if (!result.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True)
            throw new BusinessException("Meta no confirmó la suscripción de webhooks.");
    }

    public async Task VerifyWebhookFieldsAsync(CancellationToken ct, bool liveOnly = false)
    {
        var result = await RequestAsync(HttpMethod.Get, $"{Id(AppId)}/subscriptions", $"{AppId}|{AppSecret}", null, ct);
        var fields = result.GetProperty("data").EnumerateArray()
            .Where(x => x.TryGetProperty("object", out var topic) && topic.GetString() == "whatsapp_business_account")
            .SelectMany(x => x.GetProperty("fields").EnumerateArray())
            .Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.GetProperty("name").GetString()).ToHashSet();
        var required = liveOnly ? new[] { "messages", "smb_message_echoes", "account_update" }
            : new[] { "messages", "history", "smb_app_state_sync", "smb_message_echoes", "account_update" };
        if (required.Any(x => !fields.Contains(x)))
            throw new BusinessException($"Activa {string.Join(", ", required)} en los webhooks de la aplicación Meta.");
    }

    public async Task<string?> ReadPhoneCallbackAsync(string phoneId, string token, CancellationToken ct)
    {
        var result = await RequestAsync(HttpMethod.Get, $"{Id(phoneId)}?fields=webhook_configuration", token, null, ct);
        if (!result.TryGetProperty("id", out var id) || id.GetString() != phoneId
            || !result.TryGetProperty("webhook_configuration", out var settings) || settings.ValueKind != JsonValueKind.Object)
            throw new BusinessException("Meta no devolvió la configuración del webhook de Santander.");
        return settings.TryGetProperty("phone_number", out var callback) ? callback.GetString() : null;
    }

    public async Task SetPhoneCallbackAsync(string phoneId, string token, string? callback, CancellationToken ct)
    {
        var fields = new Dictionary<string, string> { ["override_callback_uri"] = callback ?? "" };
        if (callback is not null) fields["verify_token"] = VerifyToken;
        var result = await RequestAsync(HttpMethod.Post, Id(phoneId), token,
            JsonContent.Create(new { webhook_configuration = fields }), ct);
        if (!result.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True)
            throw new BusinessException("Meta no confirmó el callback de Santander.");
    }

    public async Task<string> SyncAsync(string phoneId, string token, string type, CancellationToken ct)
    {
        var result = await RequestAsync(HttpMethod.Post, $"{Id(phoneId)}/smb_app_data", token,
            JsonContent.Create(new { messaging_product = "whatsapp", sync_type = type }), ct);
        return result.GetProperty("request_id").GetString() ?? throw new BusinessException("Meta no confirmó la sincronización.");
    }

    public async Task VerifySubscriptionAsync(string accountId, string token, CancellationToken ct)
    {
        if (!await IsSubscribedAsync(accountId, token, ct))
            throw new BusinessException("La aplicación no está suscrita a los eventos de esta cuenta.");
    }

    private async Task<bool> IsSubscribedAsync(string accountId, string token, CancellationToken ct)
    {
        var result = await RequestAsync(HttpMethod.Get, $"{Id(accountId)}/subscribed_apps", token, null, ct);
        return result.GetProperty("data").EnumerateArray().Any(x => x.TryGetProperty("whatsapp_business_api_data", out var app)
            && app.TryGetProperty("id", out var id) && id.GetString() == AppId);
    }

    private async Task<JsonElement> RequestAsync(HttpMethod method, string path, string? token, HttpContent? content, CancellationToken ct)
    {
        if (!Configured) throw new BusinessException("La conexión guiada de Meta aún no está configurada en el servidor.");
        using var request = new HttpRequestMessage(method, $"https://graph.facebook.com/{Version}/{path}") { Content = content };
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new BusinessException($"Meta rechazó la operación ({(int)response.StatusCode}). Revisa permisos y conexión de la línea.");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return document.RootElement.Clone();
    }

    private static string Id(string value) => Regex.IsMatch(value, "^[0-9]{1,64}$") ? value
        : throw new BusinessException("El identificador de Meta no es válido.");
}
