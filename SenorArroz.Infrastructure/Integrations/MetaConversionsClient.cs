using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Options;

namespace SenorArroz.Infrastructure.Integrations;

public sealed class MetaConversionsClient(HttpClient httpClient, IOptions<MetaConversionsOptions> options)
{
    private readonly MetaConversionsOptions _options = options.Value;

    public bool IsConfigured => _options.IsConfigured;

    public async Task SendPurchaseAsync(MetaPurchaseEvent purchase, CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
            throw new MetaConversionsException("Meta Conversions API no está configurada.", retryable: false);
        if (string.IsNullOrWhiteSpace(purchase.ClientUserAgent))
            throw new MetaConversionsException("El evento web no tiene el user agent original del cliente.", retryable: false);
        if (purchase.Value < 0 || purchase.Shipping < 0)
            throw new MetaConversionsException("El valor del evento Purchase no es válido para Meta CAPI.", retryable: false);

        var userData = new Dictionary<string, object>
        {
            ["ph"] = new[] { HashPhone(purchase.Phone) },
            ["client_user_agent"] = purchase.ClientUserAgent,
        };
        if (!string.IsNullOrWhiteSpace(purchase.ClientIpAddress))
            userData["client_ip_address"] = purchase.ClientIpAddress;
        if (!string.IsNullOrWhiteSpace(purchase.Fbp))
            userData["fbp"] = purchase.Fbp;
        if (!string.IsNullOrWhiteSpace(purchase.Fbc))
            userData["fbc"] = purchase.Fbc;

        var contents = purchase.Contents
            .Where(x => x.Quantity > 0)
            .Select(x => new { id = x.ProductId.ToString(), quantity = x.Quantity })
            .ToArray();
        if (contents.Length == 0)
            throw new MetaConversionsException("El evento Purchase no contiene productos válidos para Meta CAPI.", retryable: false);

        var customData = new Dictionary<string, object>
        {
            ["currency"] = "COP",
            ["value"] = purchase.Value,
            ["shipping"] = purchase.Shipping,
            ["content_type"] = "product",
            ["content_ids"] = contents.Select(x => x.id).ToArray(),
            ["contents"] = contents,
            ["num_items"] = contents.Sum(x => x.quantity),
            ["transaction_id"] = purchase.OrderId.ToString(),
            ["order_id"] = purchase.OrderId,
            ["branch_id"] = purchase.BranchId,
            ["payment_type"] = purchase.PaymentType,
        };

        var serverEvent = new Dictionary<string, object>
        {
            ["event_name"] = "Purchase",
            ["event_time"] = new DateTimeOffset(NormalizeUtc(purchase.EventTime)).ToUnixTimeSeconds(),
            ["event_id"] = $"purchase-{purchase.OrderId}",
            ["action_source"] = "website",
            ["event_source_url"] = _options.EventSourceUrl.Trim(),
            ["user_data"] = userData,
            ["custom_data"] = customData,
        };
        var payload = new Dictionary<string, object> { ["data"] = new[] { serverEvent } };
        if (!string.IsNullOrWhiteSpace(_options.TestEventCode))
            payload["test_event_code"] = _options.TestEventCode.Trim();

        var version = NormalizeVersion(_options.GraphApiVersion);
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
                $"Meta Conversions API respondió {(int)response.StatusCode}: {SafeError(responseBody)}",
                retryable);
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (!document.RootElement.TryGetProperty("events_received", out var received)
                || received.ValueKind != JsonValueKind.Number
                || received.GetInt32() < 1)
                throw new MetaConversionsException("Meta Conversions API no confirmó la recepción del evento.", retryable: true);
        }
        catch (JsonException exception)
        {
            throw new MetaConversionsException("Meta Conversions API devolvió una respuesta inválida.", retryable: true, exception);
        }
    }

    internal static string HashPhone(string phone)
    {
        var digits = new string((phone ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length == 10 && digits[0] == '3') digits = $"57{digits}";
        if (digits.Length != 12 || !digits.StartsWith("573", StringComparison.Ordinal))
            throw new MetaConversionsException("El teléfono del cliente no es un celular colombiano válido para Meta CAPI.", retryable: false);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(digits))).ToLowerInvariant();
    }

    private static DateTime NormalizeUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    private static string NormalizeVersion(string? value)
    {
        var version = string.IsNullOrWhiteSpace(value) ? "v25.0" : value.Trim();
        return version.StartsWith('v') ? version : $"v{version}";
    }

    private static string SafeError(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody)) return "sin detalle";
        var value = responseBody.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return value.Length <= 500 ? value : value[..500];
    }
}

public sealed class MetaConversionsException : Exception
{
    public MetaConversionsException(string message, bool retryable, Exception? innerException = null)
        : base(message, innerException) => Retryable = retryable;

    public bool Retryable { get; }
}

public sealed record MetaPurchaseEvent(
    int OrderId,
    DateTime EventTime,
    string Phone,
    decimal Value,
    int Shipping,
    int BranchId,
    string PaymentType,
    IReadOnlyCollection<MetaPurchaseContent> Contents,
    string? ClientUserAgent,
    string? ClientIpAddress,
    string? Fbp,
    string? Fbc);

public sealed record MetaPurchaseContent(int ProductId, int Quantity);
