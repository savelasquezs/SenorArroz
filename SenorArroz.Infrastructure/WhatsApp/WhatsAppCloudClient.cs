using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Models;
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
                return new WhatsAppCloudTestResult(false, null, CreateMetaHttpError("test_connection", body, response.StatusCode, accessToken));

            using var document = JsonDocument.Parse(body);
            var display = TryGetString(document.RootElement, "display_phone_number");
            return new WhatsAppCloudTestResult(true, display, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new WhatsAppCloudTestResult(false, null, CreateClientError("test_connection", ex, accessToken));
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
                return new WhatsAppCloudSendResult(false, null, CreateMetaHttpError("send_text", body, response.StatusCode, accessToken));

            using var document = JsonDocument.Parse(body);
            var messageId = TryGetFirstMessageId(document.RootElement);
            return new WhatsAppCloudSendResult(true, messageId, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new WhatsAppCloudSendResult(false, null, CreateClientError("send_text", ex, accessToken));
        }
    }

    public async Task<WhatsAppCloudSendResult> SendUrlButtonMessageAsync(string phoneNumberId, string accessToken, string toPhoneNumber, string body, string buttonText, string url, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildGraphUrl($"{phoneNumberId}/messages"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(new { messaging_product = "whatsapp", to = toPhoneNumber, type = "interactive", interactive = new { type = "cta_url", body = new { text = body }, action = new { name = "cta_url", parameters = new { display_text = buttonText, url } } } });
        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken); var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode) return new(false, null, CreateMetaHttpError("send_url_button", json, response.StatusCode, accessToken));
            using var document = JsonDocument.Parse(json); return new(true, TryGetFirstMessageId(document.RootElement), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        { return new(false, null, CreateClientError("send_url_button", ex, accessToken)); }
    }

    public async Task<WhatsAppCloudSendResult> SendReplyButtonsMessageAsync(string phoneNumberId,string accessToken,string toPhoneNumber,string body,IReadOnlyList<WhatsAppReplyButton> buttons,CancellationToken cancellationToken=default)
    {
        var safe=buttons.Take(3).Select(x=>new{type="reply",reply=new{id=x.Id[..Math.Min(256,x.Id.Length)],title=x.Title[..Math.Min(20,x.Title.Length)]}}).ToArray();
        if(safe.Length==0)return new(false,null,"Se requiere al menos un botón.");
        using var request=new HttpRequestMessage(HttpMethod.Post,BuildGraphUrl($"{phoneNumberId}/messages"));request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",accessToken);request.Content=JsonContent.Create(new{messaging_product="whatsapp",to=toPhoneNumber,type="interactive",interactive=new{type="button",body=new{text=body},action=new{buttons=safe}}});
        try{using var response=await _httpClient.SendAsync(request,cancellationToken);var json=await response.Content.ReadAsStringAsync(cancellationToken);if(!response.IsSuccessStatusCode)return new(false,null,CreateMetaHttpError("send_reply_buttons",json,response.StatusCode,accessToken));using var document=JsonDocument.Parse(json);return new(true,TryGetFirstMessageId(document.RootElement),null);}catch(Exception ex)when(ex is HttpRequestException or TaskCanceledException or JsonException){return new(false,null,CreateClientError("send_reply_buttons",ex,accessToken));}
    }

    public async Task<WhatsAppCloudSendResult> SendImageLinkMessageAsync(string phoneNumberId, string accessToken, string toPhoneNumber, string imageUrl, string? caption, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildGraphUrl($"{phoneNumberId}/messages")); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(new { messaging_product = "whatsapp", to = toPhoneNumber, type = "image", image = new { link = imageUrl, caption } });
        try { using var response = await _httpClient.SendAsync(request, cancellationToken); var json = await response.Content.ReadAsStringAsync(cancellationToken); if (!response.IsSuccessStatusCode) return new(false, null, CreateMetaHttpError("send_image_link", json, response.StatusCode, accessToken)); using var doc = JsonDocument.Parse(json); return new(true, TryGetFirstMessageId(doc.RootElement), null); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { return new(false, null, CreateClientError("send_image_link", ex, accessToken)); }
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
                return new WhatsAppCloudTemplateSyncResult(false, [], CreateMetaHttpError("get_templates", body, response.StatusCode, accessToken));

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
            return new WhatsAppCloudTemplateSyncResult(false, [], CreateClientError("get_templates", ex, accessToken));
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
                return new WhatsAppCloudSendResult(false, null, CreateMetaHttpError("send_template", body, response.StatusCode, accessToken));

            using var document = JsonDocument.Parse(body);
            return new WhatsAppCloudSendResult(true, TryGetFirstMessageId(document.RootElement), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new WhatsAppCloudSendResult(false, null, CreateClientError("send_template", ex, accessToken));
        }
    }

    public async Task<WhatsAppCloudSendResult> SendAuthenticationTemplateMessageAsync(
        string phoneNumberId,
        string accessToken,
        string toPhoneNumber,
        string templateName,
        string language,
        string code,
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
                components = new object[]
                {
                    new
                    {
                        type = "body",
                        parameters = new[] { new { type = "text", text = code } }
                    },
                    new
                    {
                        type = "button",
                        sub_type = "url",
                        index = "0",
                        parameters = new[] { new { type = "text", text = code } }
                    }
                }
            }
        });

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new WhatsAppCloudSendResult(false, null, CreateMetaHttpError("send_authentication_template", body, response.StatusCode, accessToken));

            using var document = JsonDocument.Parse(body);
            return new WhatsAppCloudSendResult(true, TryGetFirstMessageId(document.RootElement), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new WhatsAppCloudSendResult(false, null, CreateClientError("send_authentication_template", ex, accessToken));
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
                return new WhatsAppCloudUploadMediaResult(false, null, CreateMetaHttpError("upload_media", body, response.StatusCode, accessToken));

            using var document = JsonDocument.Parse(body);
            return new WhatsAppCloudUploadMediaResult(true, TryGetString(document.RootElement, "id"), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new WhatsAppCloudUploadMediaResult(false, null, CreateClientError("upload_media", ex, accessToken));
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
                return new WhatsAppCloudSendResult(false, null, CreateMetaHttpError("send_media", body, response.StatusCode, accessToken));

            using var document = JsonDocument.Parse(body);
            return new WhatsAppCloudSendResult(true, TryGetFirstMessageId(document.RootElement), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new WhatsAppCloudSendResult(false, null, CreateClientError("send_media", ex, accessToken));
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
                return new WhatsAppCloudMediaInfoResult(false, mediaId, null, null, null, null, CreateMetaHttpError("get_media_info", body, response.StatusCode, accessToken));

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
            return new WhatsAppCloudMediaInfoResult(false, mediaId, null, null, null, null, CreateClientError("get_media_info", ex, accessToken));
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
                return new WhatsAppCloudDownloadedMedia(false, null, null, CreateMetaHttpError("download_media", body, response.StatusCode, accessToken));
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var contentType = response.Content.Headers.ContentType?.MediaType;
            return new WhatsAppCloudDownloadedMedia(true, bytes, contentType, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new WhatsAppCloudDownloadedMedia(false, null, null, CreateClientError("download_media", ex, accessToken));
        }
    }

    private string BuildGraphUrl(string path)
    {
        var baseUrl = (_options.BaseUrl ?? "https://graph.facebook.com").TrimEnd('/');
        var version = string.IsNullOrWhiteSpace(_options.GraphApiVersion) ? "v20.0" : _options.GraphApiVersion.Trim('/');
        return $"{baseUrl}/{version}/{path.TrimStart('/')}";
    }

    private string CreateMetaHttpError(string operation, string body, HttpStatusCode statusCode, string accessToken)
    {
        var safeBody = Sanitize(body, accessToken);
        var providerMessage = ExtractMetaProviderMessage(safeBody);
        _logger.LogWarning(
            "Meta WhatsApp request failed Operation={Operation} StatusCode={StatusCode} ProviderError={ProviderError} ResponseBody={ResponseBody}",
            operation,
            (int)statusCode,
            providerMessage,
            safeBody);

        return $"Meta WhatsApp HTTP {(int)statusCode}: {providerMessage} | body: {safeBody}";
    }

    private string CreateClientError(string operation, Exception exception, string accessToken)
    {
        var failureType = exception switch
        {
            TaskCanceledException => "timeout",
            HttpRequestException => "network_error",
            JsonException => "invalid_response",
            _ => "client_error"
        };
        var safeMessage = Sanitize(exception.Message, accessToken);
        _logger.LogWarning(
            "Meta WhatsApp client failure Operation={Operation} FailureType={FailureType} ExceptionType={ExceptionType} Error={ProviderError}",
            operation,
            failureType,
            exception.GetType().Name,
            safeMessage);
        return $"Meta WhatsApp {failureType}: {safeMessage}";
    }

    private static string ExtractMetaProviderMessage(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                var message = TryGetString(error, "message");
                var type = TryGetString(error, "type");
                var userMessage = TryGetString(error, "error_user_msg");
                var code = error.TryGetProperty("code", out var codeElement) ? codeElement.ToString() : null;
                var subcode = error.TryGetProperty("error_subcode", out var subcodeElement) ? subcodeElement.ToString() : null;
                var details = string.Join(", ", new[]
                {
                    type is null ? null : $"type={type}",
                    code is null ? null : $"code={code}",
                    subcode is null ? null : $"subcode={subcode}"
                }.Where(x => !string.IsNullOrWhiteSpace(x)));
                var primary = string.Join(" ", new[] { message, userMessage }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());
                if (!string.IsNullOrWhiteSpace(primary))
                    return string.IsNullOrWhiteSpace(details) ? primary : $"{primary} [{details}]";
            }
        }
        catch (JsonException)
        {
            // The raw provider body is retained by the caller.
        }

        return string.IsNullOrWhiteSpace(body) ? "Respuesta de error vacía de Meta." : body;
    }

    private static string Sanitize(string? value, string? accessToken)
    {
        if (string.IsNullOrEmpty(value))
            return "<empty>";

        var safe = value;
        if (!string.IsNullOrWhiteSpace(accessToken))
            safe = safe.Replace(accessToken, "[REDACTED]", StringComparison.Ordinal);
        safe = Regex.Replace(safe, "(?i)(Authorization\\s*[:=]\\s*Bearer\\s+)[^\\s\\\"']+", "$1[REDACTED]");
        safe = Regex.Replace(safe, "(?i)(\\\"access_token\\\"\\s*:\\s*\\\")[^\\\"]*(\\\")", "$1[REDACTED]$2");
        safe = Regex.Replace(safe, "(?i)(access_token=)[^&\\s\\\"']+", "$1[REDACTED]");
        return safe;
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
