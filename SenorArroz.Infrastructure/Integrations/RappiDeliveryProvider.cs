using System.Net.Http.Json;
using System.Text.Json;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Integrations;

public sealed class RappiDeliveryProvider(HttpClient httpClient) : IRappiDeliveryProvider
{
    public async Task<RappiConnectionResult> TestConnectionAsync(DeliveryAppConnection connection, string clientSecret, CancellationToken ct)
    {
        if (IsSimulator(connection)) return new(true);
        var token = await GetTokenAsync(connection, clientSecret, ct);
        return token.Error is null ? new(true) : new(false, token.Error);
    }

    public async Task<IReadOnlyList<RappiCatalogItem>> GetCatalogAsync(DeliveryAppConnection connection, string clientSecret, CancellationToken ct)
    {
        if (IsSimulator(connection))
            return [new("rappi-demo-1", "RAPPI-DEMO-1", "Producto demostración Rappi", "product")];

        var token = await GetTokenAsync(connection, clientSecret, ct);
        if (token.Error is not null) throw new InvalidOperationException(token.Error);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl(connection)}/restaurants/menu/v1/stores/{Uri.EscapeDataString(connection.ExternalStoreId)}/menu");
        request.Headers.TryAddWithoutValidation("x-authorization", $"Bearer {token.AccessToken}");
        using var response = await httpClient.SendAsync(request, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Rappi respondió {(int)response.StatusCode}: {json}");
        using var doc = JsonDocument.Parse(json);
        var result = new Dictionary<string, RappiCatalogItem>(StringComparer.OrdinalIgnoreCase);
        VisitCatalog(doc.RootElement, result);
        return result.Values.ToList();
    }

    public async Task<RappiWebhookResult> ConfigureWebhookAsync(DeliveryAppConnection c, string secret, string webhookUrl, CancellationToken ct)
    {
        if (IsSimulator(c)) return new(true, "simulator-webhook-secret");
        var token = await GetTokenAsync(c, secret, ct);
        if (token.Error is not null) return new(false, Error: token.Error);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl(c)}/api/v2/restaurants-integrations-public-api/webhook");
        request.Headers.TryAddWithoutValidation("x-authorization", $"Bearer {token.AccessToken}");
        request.Content = JsonContent.Create(new { event_name = "NEW_ORDER", url = webhookUrl, stores = new[] { c.ExternalStoreId } });
        using var response = await httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) return new(false, Error: $"No se pudo configurar webhook ({(int)response.StatusCode}): {body}");
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        var generatedSecret = doc.RootElement.TryGetProperty("secret", out var s) ? s.GetString() : null;
        return new(true, generatedSecret);
    }

    public Task<RappiOperationResult> AcceptOrderAsync(DeliveryAppConnection c, string secret, string orderId, int cooking, CancellationToken ct) =>
        SendOperationAsync(c, secret, HttpMethod.Put, $"/restaurants/orders/v1/stores/{c.ExternalStoreId}/orders/{orderId}/cooking_time/{cooking}/take", ct);

    public Task<RappiOperationResult> RejectOrderAsync(DeliveryAppConnection c, string secret, string orderId, CancellationToken ct) =>
        SendOperationAsync(c, secret, HttpMethod.Put, $"/restaurants/orders/v1/stores/{c.ExternalStoreId}/orders/{orderId}/cancel_type/OTHER/reject", ct);

    public Task<RappiOperationResult> ReadyForPickupAsync(DeliveryAppConnection c, string secret, string orderId, CancellationToken ct) =>
        SendOperationAsync(c, secret, HttpMethod.Post, $"/restaurants/orders/v1/stores/{c.ExternalStoreId}/orders/{orderId}/ready-for-pickup", ct);

    private async Task<RappiOperationResult> SendOperationAsync(DeliveryAppConnection c, string secret, HttpMethod method, string path, CancellationToken ct)
    {
        if (IsSimulator(c)) return new(true);
        var token = await GetTokenAsync(c, secret, ct);
        if (token.Error is not null) return new(false, token.Error);
        using var request = new HttpRequestMessage(method, BaseUrl(c) + path);
        request.Headers.TryAddWithoutValidation("x-authorization", $"Bearer {token.AccessToken}");
        request.Content = JsonContent.Create(new { });
        using var response = await httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return response.IsSuccessStatusCode ? new(true) : new(false, $"Rappi respondió {(int)response.StatusCode}: {body}");
    }

    private async Task<(string? AccessToken, string? Error)> GetTokenAsync(DeliveryAppConnection c, string secret, CancellationToken ct)
    {
        using var response = await httpClient.PostAsJsonAsync($"{BaseUrl(c)}/restaurants/auth/v1/token/login/integrations", new { client_id = c.ClientId, client_secret = secret }, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) return (null, $"Autenticación Rappi falló ({(int)response.StatusCode}).");
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("access_token", out var token) && !string.IsNullOrWhiteSpace(token.GetString())
            ? (token.GetString(), null)
            : (null, "Rappi no devolvió access_token.");
    }

    private static string BaseUrl(DeliveryAppConnection c) => c.Environment == "production" ? "https://api.rappi.com" : "https://api.dev.rappi.com";
    private static bool IsSimulator(DeliveryAppConnection c) => c.Environment == "simulator";

    private static void VisitCatalog(JsonElement node, IDictionary<string, RappiCatalogItem> result)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            var id = GetString(node, "id") ?? GetString(node, "product_id");
            var sku = GetString(node, "sku");
            var name = GetString(node, "name");
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name) && (!string.IsNullOrWhiteSpace(sku) || node.TryGetProperty("type", out _)))
            {
                var type = GetString(node, "type") ?? "product";
                result[$"{type}:{id}"] = new(id, sku ?? id, name, type, true);
            }
            foreach (var property in node.EnumerateObject()) VisitCatalog(property.Value, result);
        }
        else if (node.ValueKind == JsonValueKind.Array)
            foreach (var child in node.EnumerateArray()) VisitCatalog(child, result);
    }

    private static string? GetString(JsonElement e, string name) => e.TryGetProperty(name, out var value) ? value.ToString() : null;
}
