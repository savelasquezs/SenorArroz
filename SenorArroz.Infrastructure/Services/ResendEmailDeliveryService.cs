using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Models;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.Infrastructure.Services;

public class ResendEmailDeliveryService
{
    private const string ProviderName = "resend";
    private readonly HttpClient _httpClient;
    private readonly ILogger<ResendEmailDeliveryService> _logger;
    private readonly string _apiKey;
    private readonly string _fromEmail;
    private readonly string _fromName;
    private readonly IApplicationDbContext? _db;

    public ResendEmailDeliveryService(HttpClient httpClient, IConfiguration configuration, ILogger<ResendEmailDeliveryService> logger, IApplicationDbContext? db = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _db = db;
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
            return EmailSendResult.Fail(ProviderName, error, message.MessageType == "electronic_invoice");
        }

        try
        {
            var recipients = (JsonSerializer.Deserialize<List<string>>(message.ToEmailsJson) ?? [])
                .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (recipients.Count == 0) return EmailSendResult.Fail(ProviderName, "El mensaje no tiene destinatarios.", true);

            var payload = new Dictionary<string, object>
            {
                ["from"] = $"{_fromName} <{_fromEmail}>",
                ["to"] = recipients,
                ["subject"] = message.Subject,
                [message.IsHtml ? "html" : "text"] = message.Body
            };
            if (message.MessageType == "electronic_invoice")
            {
                var document = _db is null ? null : await _db.ElectronicInvoiceDocuments.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == message.RelatedEntityId && x.TenantId == message.TenantId, cancellationToken);
                if (document is null || document.Status is not ("issued" or "credited") || document.PdfVersion < 1 || document.Pdf is null || document.Xml is null)
                    return EmailSendResult.Fail(ProviderName, "Faltan archivos fiscales validados para adjuntar.", true);
                using var metadata = JsonDocument.Parse(message.MetadataJson);
                var info = metadata.RootElement;
                if (info.GetProperty("pdfHash").GetString() != InvoiceDeliveryService.Hash(document.Pdf)
                    || info.GetProperty("xmlHash").GetString() != InvoiceDeliveryService.Hash(document.Xml))
                    return EmailSendResult.Fail(ProviderName, "Los archivos cambiaron después de preparar el correo. Revisa el documento antes de reenviar.", true);
                var name = new string((info.GetProperty("fromName").GetString() ?? _fromName).Where(x => !char.IsControl(x) && x != '<' && x != '>').ToArray());
                var fromEmail = info.TryGetProperty("fromEmail", out var address) && !string.IsNullOrWhiteSpace(address.GetString()) ? address.GetString() : _fromEmail;
                payload["from"] = new MailAddress(fromEmail!, name).ToString();
                if (info.TryGetProperty("replyTo", out var reply) && !string.IsNullOrWhiteSpace(reply.GetString()))
                    payload["reply_to"] = reply.GetString()!;
                var filename = new string((info.GetProperty("filename").GetString() ?? "factura").Where(x => char.IsAsciiLetterOrDigit(x) || x is '-' or '_').ToArray());
                var attachments = new List<object>
                {
                    new { filename = filename + ".pdf", content = Convert.ToBase64String(document.Pdf), content_type = "application/pdf" },
                    new { filename = filename + ".xml", content = Convert.ToBase64String(document.Xml), content_type = "application/xml" }
                };
                if (document.BrandingId.HasValue)
                {
                    var branding = await _db!.ElectronicInvoiceBrandings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == document.BrandingId && x.TenantId == message.TenantId, cancellationToken);
                    if (branding is null || !info.TryGetProperty("logoHash", out var logoHash) || logoHash.GetString() != InvoiceDeliveryService.Hash(branding.Logo))
                        return EmailSendResult.Fail(ProviderName, "El logo no coincide con la identidad congelada del correo.", true);
                    var png = branding.Logo[0] == 137;
                    attachments.Add(new { filename = png ? "logo.png" : "logo.jpg", content = Convert.ToBase64String(branding.Logo), content_type = png ? "image/png" : "image/jpeg", content_id = "invoice-logo" });
                }
                payload["attachments"] = attachments;
                payload["tags"] = new[] { new { name = "outbox_id", value = message.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) } };
            }
            using var request = new HttpRequestMessage(HttpMethod.Post, "emails") { Content = JsonContent.Create(payload) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            request.Headers.TryAddWithoutValidation("Idempotency-Key", message.MessageType == "electronic_invoice" ? $"email-outbox-{message.TenantId}-{message.Id}" : $"email-outbox-{message.Id}");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = $"Resend respondió HTTP {(int)response.StatusCode} ({response.ReasonPhrase}): {Truncate(responseBody, 2000)}";
                _logger.LogError("Resend rejected queued email {MessageId}. StatusCode: {StatusCode}. Response: {Response}", message.Id, (int)response.StatusCode, Truncate(responseBody, 2000));
                return EmailSendResult.Fail(ProviderName, error, (int)response.StatusCode is >= 400 and < 500 and not (408 or 409 or 429));
            }

            _logger.LogInformation("Queued email {MessageId} accepted by Resend. ResendId: {ResendId}. Recipients: {Recipients}", message.Id, TryGetResendId(responseBody) ?? "unknown", string.Join(", ", recipients));
            return EmailSendResult.Ok(ProviderName, TryGetResendId(responseBody));
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
