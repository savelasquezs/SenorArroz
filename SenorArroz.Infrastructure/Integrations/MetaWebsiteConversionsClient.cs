using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Options;

namespace SenorArroz.Infrastructure.Integrations;

public sealed class MetaWebsiteConversionsClient(HttpClient httpClient, IOptions<MetaConversionsOptions> options)
{
    private static readonly IReadOnlyDictionary<string, string> EventNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["view_item"] = "ViewContent",
            ["add_to_cart"] = "AddToCart",
            ["begin_checkout"] = "InitiateCheckout",
            ["add_payment_info"] = "AddPaymentInfo",
        };

    private readonly MetaConversionsOptions _options = options.Value;

    public bool IsConfigured => _options.IsConfigured;

    public bool Supports(string eventName) => EventNames.ContainsKey(eventName);

    public async Task SendAsync(MetaWebsiteFunnelEvent sourceEvent, CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
            throw new MetaConversionsException("Meta Conversions API no está configurada.", retryable: false);
        if (!EventNames.TryGetValue(sourceEvent.EventName, out var metaEventName))
            throw new MetaConversionsException("El evento no está habilitado para Meta CAPI.", retryable: false);
        if (string.IsNullOrWhiteSpace(sourceEvent.EventId) || sourceEvent.EventId.Length > 160)
            throw new MetaConversionsException("El event_id de Meta CAPI no es válido.", retryable: false);
        if (string.IsNullOrWhiteSpace(sourceEvent.ClientUserAgent))
            throw new MetaConversionsException("El evento web no tiene user agent del cliente.", retryable: false);

        var userData = new Dictionary<string, object>
        {
            ["client_user_agent"] = sourceEvent.ClientUserAgent,
        };
        if (!string.IsNullOrWhiteSpace(sourceEvent.ClientIpAddress))
            userData["client_ip_address"] = sourceEvent.ClientIpAddress;
        if (!string.IsNullOrWhiteSpace(sourceEvent.Fbp))
            userData["fbp"] = sourceEvent.Fbp;
        if (!string.IsNullOrWhiteSpace(sourceEvent.Fbc))
            userData["fbc"] = sourceEvent.Fbc;

        var customData = new Dictionary<string, object>
        {
            ["currency"] = "COP",
            ["value"] = Math.Max(0, sourceEvent.Value),
        };
        if (sourceEvent.BranchId > 0) customData["branch_id"] = sourceEvent.BranchId;
        if (!string.IsNullOrWhiteSpace(sourceEvent.FulfillmentType))
            customData["fulfillment_type"] = sourceEvent.FulfillmentType;
        if (!string.IsNullOrWhiteSpace(sourceEvent.PaymentType))
            customData["payment_type"] = sourceEvent.PaymentType;

        var sourceUrl = _options.EventSourceUrl.Trim().TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(sourceEvent.Path) && sourceEvent.Path.StartsWith('/'))
            sourceUrl += sourceEvent.Path;

        var serverEvent = new Dictionary<string, object>
        {
            ["event_name"] = metaEventName,
            ["event_time"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["event_id"] = sourceEvent.EventId,
            ["action_source"] = "website",
            ["event_source_url"] = sourceUrl,
            ["user_data"] = userData,
            ["custom_data"] = customData,
        };
        var payload = new Dictionary<string, object> { ["data"] = new[] { serverEvent } };
        if (!string.IsNullOrWhiteSpace(_options.TestEventCode))
            payload["test_event_code"] = _options.TestEventCode.Trim();

        var version = string.IsNullOrWhiteSpace(_options.GraphApiVersion) ? "v25.0" : _options.GraphApiVersion.Trim();
        if (!version.StartsWith('v')) version = $"v{version}";
        var pixelId = Uri.EscapeDataString(_options.PixelId.Trim());

        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://graph.facebook.com/{version}/{pixelId}/events")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken.Trim());

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var retryable = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
            throw new MetaConversionsException(
                $"Meta Conversions API respondió {(int)response.StatusCode}.",
                retryable);
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (!document.RootElement.TryGetProperty("events_received", out var received)
                || received.ValueKind != JsonValueKind.Number
                || received.GetInt32() < 1)
                throw new MetaConversionsException("Meta CAPI no confirmó el evento.", retryable: true);
        }
        catch (JsonException exception)
        {
            throw new MetaConversionsException("Meta CAPI devolvió una respuesta inválida.", retryable: true, exception);
        }
    }
}

public sealed record MetaWebsiteFunnelEvent(
    string EventName,
    string EventId,
    string Path,
    decimal Value,
    int BranchId,
    string? FulfillmentType,
    string? PaymentType,
    string ClientUserAgent,
    string? ClientIpAddress,
    string? Fbp,
    string? Fbc);
