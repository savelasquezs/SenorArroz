using System.Text.Json;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Models;

namespace SenorArroz.Infrastructure.Services;

public class SmtpEmailDeliveryService
{
    private readonly ILogger<SmtpEmailDeliveryService> _logger;
    private readonly string _smtpHost;
    private readonly int _smtpPort;
    private readonly string _smtpUsername;
    private readonly string _smtpPassword;
    private readonly string _fromEmail;
    private readonly string _fromName;
    private readonly bool _enableSsl;
    private readonly int _smtpTimeoutMs;

    public SmtpEmailDeliveryService(IConfiguration configuration, ILogger<SmtpEmailDeliveryService> logger)
    {
        _logger = logger;
        _smtpHost = configuration["EmailSettings:SmtpHost"] ?? "smtp.gmail.com";
        _smtpPort = int.Parse(configuration["EmailSettings:SmtpPort"] ?? "587");
        _smtpUsername = configuration["EmailSettings:SmtpUsername"] ?? "";
        _smtpPassword = configuration["EmailSettings:SmtpPassword"] ?? "";
        _fromEmail = configuration["EmailSettings:FromEmail"] ?? "";
        _fromName = configuration["EmailSettings:FromName"] ?? "SenorArroz";
        _enableSsl = bool.Parse(configuration["EmailSettings:EnableSsl"] ?? "true");
        _smtpTimeoutMs = int.Parse(configuration["EmailSettings:SmtpTimeoutMs"] ?? "15000");
    }

    public async Task<EmailSendResult> SendAsync(EmailOutboxMessage message, CancellationToken cancellationToken = default)
    {
        var missingSettings = new List<string>();

        if (string.IsNullOrWhiteSpace(_smtpHost))
            missingSettings.Add("EmailSettings:SmtpHost");
        if (string.IsNullOrWhiteSpace(_smtpUsername))
            missingSettings.Add("EmailSettings:SmtpUsername");
        if (string.IsNullOrWhiteSpace(_smtpPassword))
            missingSettings.Add("EmailSettings:SmtpPassword");
        if (string.IsNullOrWhiteSpace(_fromEmail))
            missingSettings.Add("EmailSettings:FromEmail");

        if (missingSettings.Count > 0)
        {
            var error = $"Falta configuración SMTP: {string.Join(", ", missingSettings)}";
            _logger.LogError("Cannot deliver queued email {MessageId}. {Error}", message.Id, error);
            return EmailSendResult.Fail("smtp", error);
        }

        try
        {
            var recipients = JsonSerializer.Deserialize<List<string>>(message.ToEmailsJson) ?? [];
            if (recipients.Count == 0)
                return EmailSendResult.Fail("smtp", "El mensaje no tiene destinatarios.");

            var mailMessage = new MimeMessage();
            mailMessage.From.Add(new MailboxAddress(_fromName, _fromEmail));
            foreach (var recipient in recipients.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                mailMessage.To.Add(MailboxAddress.Parse(recipient));
            }

            mailMessage.Subject = message.Subject;
            mailMessage.Body = new TextPart(message.IsHtml ? "html" : "plain")
            {
                Text = message.Body
            };

            cancellationToken.ThrowIfCancellationRequested();
            using var client = new SmtpClient();
            client.Timeout = _smtpTimeoutMs;

            var socketOptions = _enableSsl
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.None;

            await client.ConnectAsync(_smtpHost, _smtpPort, socketOptions, cancellationToken);
            await client.AuthenticateAsync(_smtpUsername, _smtpPassword, cancellationToken);
            await client.SendAsync(mailMessage, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            _logger.LogInformation("Queued email {MessageId} sent successfully to {Recipients}", message.Id, string.Join(", ", recipients));
            return EmailSendResult.Ok("smtp");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send queued email {MessageId}", message.Id);
            return EmailSendResult.Fail("smtp", ex.Message);
        }
    }
}
