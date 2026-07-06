using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
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

    public async Task<WhatsAppCloudTemplateSyncResult> GetMessageTemplatesAsync(
        string businessAccountId,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            BuildGraphUrl($"{businessAccountId}/message_templates?fields=id,name,language,category,status,components&limit=100"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new WhatsAppCloudTemplateSyncResult(false, [], ExtractMetaError(body, response.StatusCode));

            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return new WhatsAppCloudTemplateSyncResult(true, [], null);

            var templates = new List<WhatsAppCloudTemplate>();
            foreach (var element in data.EnumerateArray())
            {
                var id = TryGetString(element, "id");
                var name = TryGetString(element, "name");
                var language = TryGetString(element, "language");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(language))
                    continue;

                var componentsJson = element.TryGetProperty("components", out var components)
                    ? components.GetRawText()
                    : "[]";

                templates.Add(new WhatsAppCloudTemplate(
                    id,
                    name,
                    language,
                    TryGetString(element, "category") ?? string.Empty,
                    TryGetString(element, "status") ?? string.Empty,
                    componentsJson));
            }

            return new WhatsAppCloudTemplateSyncResult(true, templates, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "WhatsApp template sync failed for business account id {BusinessAccountId}", businessAccountId);
            return new WhatsAppCloudTemplateSyncResult(false, [], "No se pudieron consultar las plantillas en Meta WhatsApp Cloud API.");
        }
    }

    public async Task<WhatsAppCloudSendResult> SendTemplateMessageAsync(
        string phoneNumberId,
        string accessToken,
        string toPhoneNumber,
        string templateName,
        string language,
        IReadOnlyList<string> parameters,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildGraphUrl($"{phoneNumberId}/messages"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(new
        {
            messaging_product = "whatsapp",
            to = toPhoneNumber,
            type = "template",
            template = new
            {
                name = templateName,
                language = new { code = language },
                components = new[]
                {
                    new
                    {
                        type = "body",
                        parameters = parameters.Select(x => new { type = "text", text = x ?? string.Empty }).ToArray()
                    }
                }
            }
        });

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new WhatsAppCloudSendResult(false, null, ExtractMetaError(body, response.StatusCode));

            using var document = JsonDocument.Parse(body);
            return new WhatsAppCloudSendResult(true, TryGetFirstMessageId(document.RootElement), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "WhatsApp template send failed for phone number id {PhoneNumberId}", phoneNumberId);
            return new WhatsAppCloudSendResult(false, null, "No se pudo enviar la plantilla a Meta WhatsApp Cloud API.");
        }
    }

    public async Task<WhatsAppCloudUploadMediaResult> UploadMediaAsync(
        string phoneNumberId,
        string accessToken,
        byte[] content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildGraphUrl($"{phoneNumberId}/media"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("whatsapp"), "messaging_product");
        var bytes = new ByteArrayContent(content);
        bytes.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
        form.Add(bytes, "file", string.IsNullOrWhiteSpace(fileName) ? "archivo" : fileName);
        request.Content = form;

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new WhatsAppCloudUploadMediaResult(false, null, ExtractMetaError(body, response.StatusCode));

            using var document = JsonDocument.Parse(body);
            return new WhatsAppCloudUploadMediaResult(true, TryGetString(document.RootElement, "id"), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "WhatsApp media upload failed for phone number id {PhoneNumberId}", phoneNumberId);
            return new WhatsAppCloudUploadMediaResult(false, null, "No se pudo subir el archivo a Meta WhatsApp Cloud API.");
        }
    }

    public async Task<WhatsAppCloudSendResult> SendMediaMessageAsync(
        string phoneNumberId,
        string accessToken,
        string toPhoneNumber,
        string mediaType,
        string mediaId,
        string? caption,
        string? fileName,
        CancellationToken cancellationToken = default)
    {
        var normalizedType = NormalizeMediaType(mediaType);
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildGraphUrl($"{phoneNumberId}/messages"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(BuildMediaPayload(toPhoneNumber, normalizedType, mediaId, caption, fileName));

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new WhatsAppCloudSendResult(false, null, ExtractMetaError(body, response.StatusCode));

            using var document = JsonDocument.Parse(body);
            return new WhatsAppCloudSendResult(true, TryGetFirstMessageId(document.RootElement), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "WhatsApp media send failed for phone number id {PhoneNumberId}", phoneNumberId);
            return new WhatsAppCloudSendResult(false, null, "No se pudo enviar el archivo a Meta WhatsApp Cloud API.");
        }
    }

    public async Task<WhatsAppCloudMediaInfoResult> GetMediaInfoAsync(
        string mediaId,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildGraphUrl(mediaId));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new WhatsAppCloudMediaInfoResult(false, mediaId, null, null, null, null, ExtractMetaError(body, response.StatusCode));

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            return new WhatsAppCloudMediaInfoResult(
                true,
                TryGetString(root, "id") ?? mediaId,
                TryGetString(root, "url"),
                TryGetString(root, "mime_type"),
                TryGetString(root, "sha256"),
                TryGetLong(root, "file_size"),
                null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "WhatsApp media info failed for media id {MediaId}", mediaId);
            return new WhatsAppCloudMediaInfoResult(false, mediaId, null, null, null, null, "No se pudo obtener el archivo desde Meta.");
        }
    }

    public async Task<WhatsAppCloudDownloadedMedia> DownloadMediaAsync(
        string downloadUrl,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                return new WhatsAppCloudDownloadedMedia(false, null, null, ExtractMetaError(body, response.StatusCode));
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var contentType = response.Content.Headers.ContentType?.MediaType;
            return new WhatsAppCloudDownloadedMedia(true, bytes, contentType, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "WhatsApp media download failed.");
            return new WhatsAppCloudDownloadedMedia(false, null, null, "No se pudo descargar el archivo desde Meta.");
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

    private static long? TryGetLong(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
            return number;
        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out var parsed))
            return parsed;
        return null;
    }

    private static string? TryGetFirstMessageId(JsonElement root)
    {
        if (!root.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array)
            return null;

        var first = messages.EnumerateArray().FirstOrDefault();
        return first.ValueKind == JsonValueKind.Object ? TryGetString(first, "id") : null;
    }

    private static object BuildMediaPayload(string toPhoneNumber, string mediaType, string mediaId, string? caption, string? fileName)
    {
        var media = new MediaMessagePayload.MediaReference
        {
            Id = mediaId,
            Caption = mediaType is "image" or "video" or "document" ? NullIfWhiteSpace(caption) : null,
            Filename = mediaType == "document" ? NullIfWhiteSpace(fileName) : null
        };

        return mediaType switch
        {
            "image" => new MediaMessagePayload { To = toPhoneNumber, Type = mediaType, Image = media },
            "audio" => new MediaMessagePayload { To = toPhoneNumber, Type = mediaType, Audio = media },
            "video" => new MediaMessagePayload { To = toPhoneNumber, Type = mediaType, Video = media },
            "document" => new MediaMessagePayload { To = toPhoneNumber, Type = mediaType, Document = media },
            "sticker" => new MediaMessagePayload { To = toPhoneNumber, Type = mediaType, Sticker = media },
            _ => throw new ArgumentOutOfRangeException(nameof(mediaType), mediaType, "Tipo de media WhatsApp no soportado.")
        };
    }

    private static string NormalizeMediaType(string mediaType)
    {
        var value = (mediaType ?? string.Empty).Trim().ToLowerInvariant();
        return value is "image" or "audio" or "video" or "document" or "sticker"
            ? value
            : "document";
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class MediaMessagePayload
    {
        [JsonPropertyName("messaging_product")]
        public string MessagingProduct { get; set; } = "whatsapp";

        [JsonPropertyName("to")]
        public string To { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("image")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public MediaReference? Image { get; set; }

        [JsonPropertyName("audio")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public MediaReference? Audio { get; set; }

        [JsonPropertyName("video")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public MediaReference? Video { get; set; }

        [JsonPropertyName("document")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public MediaReference? Document { get; set; }

        [JsonPropertyName("sticker")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public MediaReference? Sticker { get; set; }

        public sealed class MediaReference
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("caption")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string? Caption { get; set; }

            [JsonPropertyName("filename")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string? Filename { get; set; }
        }
    }
}
