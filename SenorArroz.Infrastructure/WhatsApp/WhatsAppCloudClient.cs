using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;

namespace SenorArroz.Infrastructure.WhatsApp;

public class WhatsAppCloudClient : IWhatsAppCloudClient
{
    private readonly HttpClient _httpClient;
    private readonly WhatsAppCloudOptions _options;
    private readonly ILogger<WhatsAppCloudClient> _logger;

    public WhatsAppCloudClient(
        HttpClient httpClient,
        IOptions<WhatsAppCloudOptions> options,
        ILogger<WhatsAppCloudClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<WhatsAppCloudTestResult> TestConnectionAsync(
        string phoneNumberId,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            BuildGraphUrl($"{phoneNumberId}?fields=id,display_phone_number,verified_name"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new WhatsAppCloudTestResult(false, null, ExtractMetaError(body, response.StatusCode));

            using var document = JsonDocument.Parse(body);
            var display = TryGetString(document.RootElement, "display_phone_number");
            return new WhatsAppCloudTestResult(true, display, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "WhatsApp test connection failed for phone number id {PhoneNumberId}", phoneNumberId);
            return new WhatsAppCloudTestResult(false, null, "No se pudo conectar con Meta WhatsApp Cloud API.");
        }
    }

    public async Task<WhatsAppCloudSendResult> SendTextMessageAsync(
        string phoneNumberId,
        string accessToken,
        string toPhoneNumber,
        string text,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildGraphUrl($"{phoneNumberId}/messages"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(new
        {
            messaging_product = "whatsapp",
            to = toPhoneNumber,
            type = "text",
            text = new { body = text }
        });

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new WhatsAppCloudSendResult(false, null, ExtractMetaError(body, response.StatusCode));

            using var document = JsonDocument.Parse(body);
            var messageId = TryGetFirstMessageId(document.RootElement);
            return new WhatsAppCloudSendResult(true, messageId, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "WhatsApp text send failed for phone number id {PhoneNumberId}", phoneNumberId);
            return new WhatsAppCloudSendResult(false, null, "No se pudo enviar el mensaje a Meta WhatsApp Cloud API.");
        }
    }

    private string BuildGraphUrl(string path)
    {
        var baseUrl = (_options.BaseUrl ?? "https://graph.facebook.com").TrimEnd('/');
        var version = string.IsNullOrWhiteSpace(_options.GraphApiVersion) ? "v20.0" : _options.GraphApiVersion.Trim('/');
        return $"{baseUrl}/{version}/{path.TrimStart('/')}";
    }

    private static string ExtractMetaError(string body, System.Net.HttpStatusCode statusCode)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                var message = TryGetString(error, "message");
                var type = TryGetString(error, "type");
                var code = error.TryGetProperty("code", out var codeElement) ? codeElement.ToString() : null;
                return string.Join(" ", new[] { message, type, code is null ? null : $"(code {code})" }.Where(x => !string.IsNullOrWhiteSpace(x)));
            }
        }
        catch (JsonException)
        {
            // Fall through to generic status message.
        }

        return $"Meta respondió con HTTP {(int)statusCode}.";
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string? TryGetFirstMessageId(JsonElement root)
    {
        if (!root.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array)
            return null;

        var first = messages.EnumerateArray().FirstOrDefault();
        return first.ValueKind == JsonValueKind.Object ? TryGetString(first, "id") : null;
    }
}
