using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Models;

namespace SenorArroz.Infrastructure.Services;

public class ResendEmailDeliveryService
{
    private const string ProviderName = "resend";
    private readonly HttpClient _httpClient;
    private readonly ILogger<ResendEmailDeliveryService> _logger;
    private readonly string _apiKey;
    private readonly string _fromEmail;
    private readonly string _fromName;

    public ResendEmailDeliveryService(HttpClient httpClient, IConfiguration configuration, ILogger<ResendEmailDeliveryService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _apiKey = configuration["ResendSettings:ApiKey"] ?? string.Empty;
        _fromEmail = configuration["ResendSettings:FromEmail"] ?? string.Empty;
        _fromName = configuration["ResendSettings:FromName"] ?? "El Señor Arroz";
        var baseUrl = configuration["ResendSettings:BaseUrl"] ?? "https://api.resend.com";
        var timeoutMs = int.Parse(configuration["ResendSettings:TimeoutMs"] ?? "15000");
        _httpClient.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        _httpClient.Timeout = TimeSpan.FromMilliseconds(timeoutMs);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SenorArroz/1.0");
    }

    public async Task<EmailSendResult> SendAsync(EmailOutboxMessage message, CancellationToken cancellationToken = default)
    {
        var missingSettings = new List<string>();
        if (string.IsNullOrWhiteSpace(_apiKey)) missingSettings.Add("ResendSettings:ApiKey");
        if (string.IsNullOrWhiteSpace(_fromEmail)) missingSettings.Add("ResendSettings:FromEmail");
        if (missingSettings.Count > 0)
        {
            var error = $"Falta configuración de Resend: {string.Join(", ", missingSettings)}";
            _logger.LogError("Cannot deliver queued email {MessageId}. {Error}", message.Id, error);
            return EmailSendResult.Fail(ProviderName, error);
        }

        try
        {
            var recipients = (JsonSerializer.Deserialize<List<string>>(message.ToEmailsJson) ?? [])
                .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (recipients.Count == 0) return EmailSendResult.Fail(ProviderName, "El mensaje no tiene destinatarios.");

            var payload = new Dictionary<string, object>
            {
                ["from"] = $"{_fromName} <{_fromEmail}>",
                ["to"] = recipients,
                ["subject"] = message.Subject,
                [message.IsHtml ? "html" : "text"] = message.Body
            };
            using var request = new HttpRequestMessage(HttpMethod.Post, "emails") { Content = JsonContent.Create(payload) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            request.Headers.TryAddWithoutValidation("Idempotency-Key", $"email-outbox-{message.Id}");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = $"Resend respondió HTTP {(int)response.StatusCode} ({response.ReasonPhrase}): {Truncate(responseBody, 2000)}";
                _logger.LogError("Resend rejected queued email {MessageId}. StatusCode: {StatusCode}. Response: {Response}", message.Id, (int)response.StatusCode, Truncate(responseBody, 2000));
                return EmailSendResult.Fail(ProviderName, error);
            }

            _logger.LogInformation("Queued email {MessageId} accepted by Resend. ResendId: {ResendId}. Recipients: {Recipients}", message.Id, TryGetResendId(responseBody) ?? "unknown", string.Join(", ", recipients));
            return EmailSendResult.Ok(ProviderName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send queued email {MessageId} through Resend", message.Id);
            return EmailSendResult.Fail(ProviderName, ex.Message);
        }
    }

    private static string? TryGetResendId(string responseBody)
    {
        try { using var document = JsonDocument.Parse(responseBody); return document.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null; }
        catch (JsonException) { return null; }
    }

    private static string Truncate(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];
}
