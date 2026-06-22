
// SenorArroz.Infrastructure/Services/EmailService.cs
using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SenorArroz.Domain.Interfaces.Services;
using SenorArroz.Domain.Models;

namespace SenorArroz.Infrastructure.Services;

public class EmailService : IEmailService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailService> _logger;
    private readonly string _smtpHost;
    private readonly int _smtpPort;
    private readonly string _smtpUsername;
    private readonly string _smtpPassword;
    private readonly string _fromEmail;
    private readonly string _fromName;
    private readonly bool _enableSsl;

    public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
    {
        _configuration = configuration;
        _logger = logger;

        _smtpHost = _configuration["EmailSettings:SmtpHost"] ?? "smtp.gmail.com";
        _smtpPort = int.Parse(_configuration["EmailSettings:SmtpPort"] ?? "587");
        _smtpUsername = _configuration["EmailSettings:SmtpUsername"] ?? "";
        _smtpPassword = _configuration["EmailSettings:SmtpPassword"] ?? "";
        _fromEmail = _configuration["EmailSettings:FromEmail"] ?? "";
        _fromName = _configuration["EmailSettings:FromName"] ?? "SenorArroz";
        _enableSsl = bool.Parse(_configuration["EmailSettings:EnableSsl"] ?? "true");
    }

    public async Task<EmailSendResult> SendPasswordResetEmailAsync(string toEmail, string userName, string resetToken, string resetUrl)
    {
        var subject = "Recuperación de Contraseña - SenorArroz";

        var body = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <title>Recuperación de Contraseña</title>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #ff6b35; color: white; padding: 20px; text-align: center; }}
        .content {{ padding: 20px; background-color: #f9f9f9; }}
        .button {{ display: inline-block; background-color: #ff6b35; color: white; padding: 12px 24px; text-decoration: none; border-radius: 5px; margin: 20px 0; }}
        .footer {{ padding: 20px; text-align: center; font-size: 12px; color: #666; }}
        .warning {{ background-color: #fff3cd; border: 1px solid #ffeaa7; padding: 10px; border-radius: 5px; margin: 15px 0; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>🍚 SenorArroz</h1>
            <p>Recuperación de Contraseña</p>
        </div>
        
        <div class='content'>
            <h2>Hola {userName}!</h2>
            
            <p>Recibimos una solicitud para restablecer la contraseña de tu cuenta en SenorArroz.</p>
            
            <p>Haz clic en el siguiente botón para crear una nueva contraseña:</p>
            
            <div style='text-align: center;'>
                <a href='{resetUrl}?token={resetToken}&email={Uri.EscapeDataString(toEmail)}' class='button'>
                    Restablecer Contraseña
                </a>
            </div>
            
            <div class='warning'>
                <strong>⚠️ Importante:</strong>
                <ul>
                    <li>Este enlace expira en 1 hora</li>
                    <li>Solo se puede usar una vez</li>
                    <li>Si no solicitaste este cambio, ignora este correo</li>
                </ul>
            </div>
            
            <p>Si el botón no funciona, copia y pega este enlace en tu navegador:</p>
            <p style='word-break: break-all; background-color: #f0f0f0; padding: 10px; border-radius: 3px;'>
                {resetUrl}?token={resetToken}&email={Uri.EscapeDataString(toEmail)}
            </p>
        </div>
        
        <div class='footer'>
            <p>© 2025 SenorArroz. Todos los derechos reservados.</p>
            <p>Este es un correo automático, por favor no responder.</p>
        </div>
    </div>
</body>
</html>";

        return await SendEmailAsync(toEmail, subject, body, isHtml: true);
    }

    public async Task<EmailSendResult> SendPasswordResetConfirmationAsync(string toEmail, string userName)
    {
        var subject = "Contraseña Restablecida - SenorArroz";

        var body = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <title>Contraseña Restablecida</title>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #28a745; color: white; padding: 20px; text-align: center; }}
        .content {{ padding: 20px; background-color: #f9f9f9; }}
        .footer {{ padding: 20px; text-align: center; font-size: 12px; color: #666; }}
        .success {{ background-color: #d4edda; border: 1px solid #c3e6cb; padding: 15px; border-radius: 5px; margin: 15px 0; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>🍚 SenorArroz</h1>
            <p>Contraseña Restablecida</p>
        </div>
        
        <div class='content'>
            <h2>¡Hola {userName}!</h2>
            
            <div class='success'>
                <strong>✅ ¡Éxito!</strong> Tu contraseña ha sido restablecida correctamente.
            </div>
            
            <p>Tu contraseña de SenorArroz ha sido cambiada exitosamente el {DateTime.Now:dd/MM/yyyy} a las {DateTime.Now:HH:mm}.</p>
            
            <p>Si no realizaste este cambio, por favor contacta al administrador del sistema inmediatamente.</p>
            
            <p>Ya puedes iniciar sesión con tu nueva contraseña.</p>
        </div>
        
        <div class='footer'>
            <p>© 2025 SenorArroz. Todos los derechos reservados.</p>
            <p>Este es un correo automático, por favor no responder.</p>
        </div>
    </div>
</body>
</html>";

        return await SendEmailAsync(toEmail, subject, body, isHtml: true);
    }

    public async Task<EmailSendResult> SendTestEmailAsync(string toEmail, string subject, string body)
    {
        return await SendEmailAsync(toEmail, subject, body, isHtml: false);
    }

    public async Task<EmailSendResult> SendDailyMonetaryAuditEmailAsync(IReadOnlyCollection<string> toEmails, DailyMonetaryAuditEmailPayload payload)
    {
        if (toEmails.Count == 0)
            return EmailSendResult.Fail("none", "No hay destinatarios configurados para la auditoría monetaria.");

        var subject = $"Auditoria monetaria diaria - {payload.BranchName} - {payload.BusinessDate:yyyy-MM-dd}";
        var groupsHtml = string.Join("", payload.Groups.Select(group =>
            $@"<div style='margin-bottom:16px;'>
<h3 style='margin:0 0 8px 0;'>{WebUtility.HtmlEncode(group.Title)}</h3>
<p style='margin:0 0 8px 0;'>Eventos: {group.EventCount} | Diferencia neta: {group.NetDifference:N0}</p>
<ul>{string.Join("", group.Lines.Select(line => $"<li>{WebUtility.HtmlEncode(line)}</li>"))}</ul>
</div>"));

        var body = $@"
<!DOCTYPE html>
<html>
<head><meta charset='utf-8'><title>Auditoria monetaria diaria</title></head>
<body style='font-family:Arial,sans-serif;color:#222;line-height:1.5;'>
<div style='max-width:720px;margin:0 auto;padding:20px;'>
<h1 style='margin-bottom:8px;'>Auditoria monetaria diaria</h1>
<p style='margin-top:0;'>Sucursal: <strong>{WebUtility.HtmlEncode(payload.BranchName)}</strong></p>
<p>Fecha de negocio: {payload.BusinessDate:yyyy-MM-dd}</p>
<p>Periodo auditado UTC: {payload.PeriodStartUtc:yyyy-MM-dd HH:mm} a {payload.PeriodEndUtc:yyyy-MM-dd HH:mm}</p>
{groupsHtml}
</div>
</body>
</html>";

        var results = await Task.WhenAll(toEmails.Select(email => SendEmailAsync(email, subject, body, isHtml: true)));
        var firstFailure = results.FirstOrDefault(x => !x.Success);
        return firstFailure ?? EmailSendResult.Ok(results.FirstOrDefault()?.Provider ?? "unknown");
    }

    private async Task<EmailSendResult> SendEmailAsync(string toEmail, string subject, string body, bool isHtml = false)
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
            _logger.LogError("Cannot send email to {Email}. {Error}", toEmail, error);
            return EmailSendResult.Fail("smtp", error);
        }

        return await SendEmailViaSmtpAsync(toEmail, subject, body, isHtml);
    }

    private async Task<EmailSendResult> SendEmailViaSmtpAsync(string toEmail, string subject, string body, bool isHtml = false)
    {
        try
        {
            using var client = new SmtpClient(_smtpHost, _smtpPort);
            client.EnableSsl = _enableSsl;
            client.UseDefaultCredentials = false;
            client.Credentials = new NetworkCredential(_smtpUsername, _smtpPassword);

            using var message = new MailMessage();
            message.From = new MailAddress(_fromEmail, _fromName);
            message.To.Add(toEmail);
            message.Subject = subject;
            message.Body = body;
            message.IsBodyHtml = isHtml;

            await client.SendMailAsync(message);

            _logger.LogInformation("Email sent successfully to {Email}", toEmail);
            return EmailSendResult.Ok("smtp");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Email}", toEmail);
            return EmailSendResult.Fail("smtp", ex.Message);
        }
    }
}
