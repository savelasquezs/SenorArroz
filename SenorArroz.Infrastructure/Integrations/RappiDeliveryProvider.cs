using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;

namespace SenorArroz.Infrastructure.Integrations;

public sealed class RappiDeliveryProvider(
    HttpClient httpClient,
    IOptions<RappiOptions> options) : IRappiDeliveryProvider
{
    private static readonly SemaphoreSlim TokenLock = new(1, 1);
    private static string? cachedToken;
    private static DateTimeOffset cachedTokenExpiresAt;
    private readonly RappiOptions options = options.Value;

    public bool CredentialsConfigured =>
        !string.IsNullOrWhiteSpace(options.ClientId)
        && !string.IsNullOrWhiteSpace(options.ClientSecret);

    public async Task<RappiConnectionResult> TestConnectionAsync(CancellationToken ct)
    {
        if (!CredentialsConfigured)
            return new(false, Error: "Configura Rappi__ClientId y Rappi__ClientSecret.");

        var response = await SendAuthorizedAsync(
            () => new HttpRequestMessage(HttpMethod.Get, BuildUrl("stores-pa")),
            ct);
        if (!response.Success)
            return new(false, Error: response.Error);

        try
        {
            using var document = JsonDocument.Parse(response.Body!);
            var stores = ParseStores(document.RootElement);
            return new(true, stores);
        }
        catch (JsonException)
        {
            return new(false, Error: "Rappi devolvió una respuesta de tiendas inválida.");
        }
    }

    public async Task<RappiOperationResult> SetStoreIntegratedAsync(
        string storeId,
        bool integrated,
        CancellationToken ct)
    {
        var path = $"stores-pa/{Uri.EscapeDataString(storeId)}/status?integrated={integrated.ToString().ToLowerInvariant()}";
        var response = await SendAuthorizedAsync(
            () => new HttpRequestMessage(HttpMethod.Put, BuildUrl(path)),
            ct);
        return ToOperation(response);
    }

    public async Task<RappiWebhookResult> ConfigureWebhookAsync(
        string eventType,
        string webhookUrl,
        IReadOnlyCollection<string> storeIds,
        CancellationToken ct)
    {
        var response = await SendAuthorizedAsync(
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl("webhook"));
                request.Content = JsonContent.Create(new
                {
                    @event = eventType,
                    data = new[]
                    {
                        new { url = webhookUrl, stores = storeIds }
                    }
                });
                return request;
            },
            ct);

        if (!response.Success)
            return new(false, Error: response.Error);

        try
        {
            using var document = JsonDocument.Parse(response.Body!);
            var secret = FindString(document.RootElement, "secret");
            return string.IsNullOrWhiteSpace(secret)
                ? new(false, Error: $"Rappi no devolvió el secreto del webhook {eventType}.")
                : new(true, secret);
        }
        catch (JsonException)
        {
            return new(false, Error: $"Rappi devolvió una respuesta inválida al registrar {eventType}.");
        }
    }

    public async Task<RappiWebhookConfigurationResult> GetWebhookAsync(
        string eventType,
        CancellationToken ct)
    {
        var response = await SendAuthorizedAsync(
            () => new HttpRequestMessage(
                HttpMethod.Get,
                BuildUrl($"webhook/{Uri.EscapeDataString(eventType)}")),
            ct);
        if (!response.Success)
            return new(false, Error: response.Error);
        try
        {
            using var document = JsonDocument.Parse(response.Body!);
            var enabled = new List<string>();
            CollectEnabledStoreIds(document.RootElement, enabled);
            return new(true, enabled);
        }
        catch (JsonException)
        {
            return new(false, Error: $"Rappi devolvió una configuración inválida para {eventType}.");
        }
    }

    public async Task<RappiWebhookResult> ResetWebhookSecretAsync(
        string eventType,
        CancellationToken ct)
    {
        var response = await SendAuthorizedAsync(
            () => new HttpRequestMessage(
                HttpMethod.Put,
                BuildUrl($"webhook/{Uri.EscapeDataString(eventType)}/reset-secret")),
            ct);
        if (!response.Success)
            return new(false, Error: response.Error);

        try
        {
            using var document = JsonDocument.Parse(response.Body!);
            var secret = FindString(document.RootElement, "secret");
            return string.IsNullOrWhiteSpace(secret)
                ? new(false, Error: $"Rappi no devolvió el nuevo secreto del webhook {eventType}.")
                : new(true, secret);
        }
        catch (JsonException)
        {
            return new(false, Error: $"Rappi devolvió una respuesta inválida al renovar {eventType}.");
        }
    }

    public async Task<RappiOperationResult> PublishMenuAsync(RappiMenuRequest menu, CancellationToken ct)
    {
        var response = await SendAuthorizedAsync(
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl("menu"));
                request.Content = JsonContent.Create(menu);
                return request;
            },
            ct);
        return ToOperation(response);
    }

    public async Task<RappiOperationResult> GetMenuApprovalAsync(
        string storeId,
        CancellationToken ct)
    {
        var response = await SendAuthorizedAsync(
            () => new HttpRequestMessage(
                HttpMethod.Get,
                BuildUrl($"menu/approved/{Uri.EscapeDataString(storeId)}")),
            ct);
        return ToOperation(response);
    }

    public async Task<RappiOperationResult> SetAvailabilityAsync(
        IReadOnlyCollection<RappiAvailabilityRequest> stores,
        CancellationToken ct)
    {
        var payload = stores.Select(store => new
        {
            store_integration_id = store.StoreIntegrationId,
            items = new
            {
                turn_on = store.TurnOn,
                turn_off = store.TurnOff
            }
        }).ToArray();

        var response = await SendAuthorizedAsync(
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Put, BuildUrl("availability/stores/items"));
                request.Content = JsonContent.Create(payload);
                return request;
            },
            ct);
        return ToOperation(response);
    }

    public async Task<RappiOrdersResult> GetSentOrdersAsync(CancellationToken ct)
    {
        var response = await SendAuthorizedAsync(
            () => new HttpRequestMessage(HttpMethod.Get, BuildUrl("orders/status/sent")),
            ct);
        if (!response.Success)
            return new(false, Error: response.Error);

        try
        {
            using var document = JsonDocument.Parse(response.Body!);
            var source = document.RootElement;
            if (source.ValueKind == JsonValueKind.Object
                && source.TryGetProperty("orders", out var orders))
                source = orders;
            if (source.ValueKind != JsonValueKind.Array)
                return new(false, Error: "Rappi devolvió una lista de órdenes inválida.");
            return new(true, source.EnumerateArray().Select(x => x.GetRawText()).ToList());
        }
        catch (JsonException)
        {
            return new(false, Error: "Rappi devolvió una lista de órdenes inválida.");
        }
    }

    public async Task<RappiOperationResult> AcceptOrderAsync(
        string orderId,
        int cookingTimeMinutes,
        CancellationToken ct)
    {
        var path = $"orders/{Uri.EscapeDataString(orderId)}/take/{cookingTimeMinutes}";
        var response = await SendAuthorizedAsync(
            () => new HttpRequestMessage(HttpMethod.Put, BuildUrl(path)),
            ct);
        return ToOperation(response);
    }

    public async Task<RappiOperationResult> RejectOrderAsync(
        string orderId,
        string reason,
        CancellationToken ct)
    {
        var response = await SendAuthorizedAsync(
            () =>
            {
                var request = new HttpRequestMessage(
                    HttpMethod.Put,
                    BuildUrl($"orders/{Uri.EscapeDataString(orderId)}/reject"));
                request.Content = JsonContent.Create(new { reason });
                return request;
            },
            ct);
        return ToOperation(response);
    }

    public async Task<RappiOperationResult> ReadyForPickupAsync(string orderId, CancellationToken ct)
    {
        var response = await SendAuthorizedAsync(
            () => new HttpRequestMessage(
                HttpMethod.Post,
                BuildUrl($"orders/{Uri.EscapeDataString(orderId)}/ready-for-pickup")),
            ct);
        return ToOperation(response);
    }

    public async Task<RappiOrderEventsResult> GetOrderEventsAsync(string orderId, CancellationToken ct)
    {
        var response = await SendAuthorizedAsync(
            () => new HttpRequestMessage(
                HttpMethod.Get,
                BuildUrl($"orders/{Uri.EscapeDataString(orderId)}/events")),
            ct);
        if (!response.Success)
            return new(false, Error: response.Error);

        try
        {
            using var document = JsonDocument.Parse(response.Body!);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("events", out var events))
                root = events;
            var values = root.ValueKind == JsonValueKind.Array
                ? root.EnumerateArray().Select(x => x.GetRawText()).ToList()
                : [root.GetRawText()];
            return new(true, values);
        }
        catch (JsonException)
        {
            return new(false, Error: "Rappi devolvió eventos inválidos.");
        }
    }

    private async Task<RappiHttpResult> SendAuthorizedAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken ct)
    {
        var tokenResult = await GetTokenAsync(ct);
        if (!tokenResult.Success)
            return new(false, null, null, tokenResult.Error);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var request = requestFactory();
            request.Headers.TryAddWithoutValidation("x-authorization", $"bearer {tokenResult.Token}");

            try
            {
                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                var body = await response.Content.ReadAsStringAsync(ct);
                if (response.IsSuccessStatusCode)
                    return new(true, (int)response.StatusCode, body, null);

                if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
                {
                    ClearToken();
                    tokenResult = await GetTokenAsync(ct);
                    if (!tokenResult.Success)
                        return new(false, (int)response.StatusCode, null, tokenResult.Error);
                    continue;
                }

                if (IsTransient(response.StatusCode) && attempt < 2)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt)), ct);
                    continue;
                }

                return new(
                    false,
                    (int)response.StatusCode,
                    null,
                    $"Rappi respondió {(int)response.StatusCode}: {ReadSafeMessage(body)}");
            }
            catch (HttpRequestException) when (attempt < 2)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt)), ct);
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested && attempt < 2)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt)), ct);
            }
        }

        return new(false, null, null, "No fue posible comunicarse con Rappi después de varios intentos.");
    }

    private async Task<RappiTokenResult> GetTokenAsync(CancellationToken ct)
    {
        if (!CredentialsConfigured)
            return new(false, null, "Configura Rappi__ClientId y Rappi__ClientSecret.");

        if (!string.IsNullOrWhiteSpace(cachedToken) && cachedTokenExpiresAt > DateTimeOffset.UtcNow)
            return new(true, cachedToken, null);

        await TokenLock.WaitAsync(ct);
        try
        {
            if (!string.IsNullOrWhiteSpace(cachedToken) && cachedTokenExpiresAt > DateTimeOffset.UtcNow)
                return new(true, cachedToken, null);

            using var response = await httpClient.PostAsJsonAsync(options.AuthUrl, new
            {
                client_id = options.ClientId,
                client_secret = options.ClientSecret
            }, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                return new(false, null, $"Autenticación Rappi falló ({(int)response.StatusCode}).");

            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("access_token", out var tokenElement)
                || string.IsNullOrWhiteSpace(tokenElement.GetString()))
                return new(false, null, "Rappi no devolvió access_token.");

            var expiresIn = document.RootElement.TryGetProperty("expires_in", out var expires)
                && expires.TryGetInt32(out var seconds)
                ? Math.Max(60, seconds)
                : 3600;
            var safetySeconds = Math.Min(60, Math.Max(5, expiresIn / 10));
            cachedToken = tokenElement.GetString();
            cachedTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn - safetySeconds);
            return new(true, cachedToken, null);
        }
        catch (JsonException)
        {
            return new(false, null, "Rappi devolvió una respuesta de autenticación inválida.");
        }
        finally
        {
            TokenLock.Release();
        }
    }

    private string BuildUrl(string path) =>
        $"{options.ApiBaseUrl.TrimEnd('/')}/{path.TrimStart('/')}";

    private static void ClearToken()
    {
        cachedToken = null;
        cachedTokenExpiresAt = default;
    }

    private static RappiOperationResult ToOperation(RappiHttpResult result) =>
        new(result.Success, result.StatusCode, result.Error);

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.RequestTimeout
        || statusCode == HttpStatusCode.TooManyRequests
        || (int)statusCode >= 500;

    private static string ReadSafeMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "sin detalle";
        try
        {
            using var document = JsonDocument.Parse(body);
            var message = FindString(document.RootElement, "message")
                ?? FindString(document.RootElement, "error")
                ?? "respuesta rechazada";
            return message.Length <= 500 ? message : message[..500];
        }
        catch (JsonException)
        {
            return "respuesta no válida";
        }
    }

    private static IReadOnlyList<RappiStoreInfo> ParseStores(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object
            && (root.TryGetProperty("stores", out var stores)
                || root.TryGetProperty("data", out stores)))
            root = stores;

        if (root.ValueKind != JsonValueKind.Array)
            return [];

        var result = new List<RappiStoreInfo>();
        foreach (var item in root.EnumerateArray())
        {
            var storeId = GetString(item, "id")
                ?? GetString(item, "store_id")
                ?? GetString(item, "storeId")
                ?? GetString(item, "rappiId")
                ?? GetString(item, "internal_id");
            if (string.IsNullOrWhiteSpace(storeId))
                continue;
            var integrationId = GetString(item, "integration_id")
                ?? GetString(item, "integrationId")
                ?? GetString(item, "external_id")
                ?? GetString(item, "store_integration_id");
            var name = GetString(item, "name") ?? GetString(item, "store_name") ?? storeId;
            result.Add(new(storeId, integrationId, name));
        }
        return result;
    }

    private static string? FindString(JsonElement root, string name)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
            foreach (var property in root.EnumerateObject())
            {
                var found = FindString(property.Value, name);
                if (!string.IsNullOrWhiteSpace(found))
                    return found;
            }
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                var found = FindString(item, name);
                if (!string.IsNullOrWhiteSpace(found))
                    return found;
            }
        }
        return null;
    }

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
            ? value.ToString()
            : null;

    private static void CollectEnabledStoreIds(JsonElement element, ICollection<string> result)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                CollectEnabledStoreIds(item, result);
            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
            return;

        if (element.TryGetProperty("stores", out var stores))
        {
            CollectEnabledStoreIds(stores, result);
            return;
        }

        var state = GetString(element, "state");
        var storeId = GetString(element, "store_id");
        if (string.Equals(state, "ENABLE", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(storeId)
            && !result.Contains(storeId))
            result.Add(storeId);
    }

    private record RappiTokenResult(bool Success, string? Token, string? Error);
    private record RappiHttpResult(bool Success, int? StatusCode, string? Body, string? Error);
}
