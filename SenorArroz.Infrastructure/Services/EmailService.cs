
// SenorArroz.Infrastructure/Services/EmailService.cs
using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Services;
using SenorArroz.Domain.Models;

namespace SenorArroz.Infrastructure.Services;

public class EmailService : IEmailService
{
    private static readonly CultureInfo ColombianCulture = CultureInfo.GetCultureInfo("es-CO");
    private readonly IApplicationDbContext _context;
    private readonly ILogger<EmailService> _logger;
    private readonly IClock _clock;
    private readonly int _maxAttempts;
    private readonly string _logoUrl;
    private readonly string _restaurantName;
    private readonly IBackgroundWorkSignal<EmailOutboxWork>? _workSignal;

    public EmailService(
        IApplicationDbContext context,
        IConfiguration configuration,
        ILogger<EmailService> logger,
        IClock clock,
        IBackgroundWorkSignal<EmailOutboxWork>? workSignal = null)
    {
        _context = context;
        _logger = logger;
        _clock = clock;
        _workSignal = workSignal;
        _maxAttempts = int.Parse(configuration["EmailSettings:MaxAttempts"] ?? "5");
        _logoUrl = configuration["Branding:EmailLogoUrl"] ?? "https://senorarroz.up.railway.app/favicon.png";
        _restaurantName = configuration["Branding:RestaurantDisplayName"] ?? "El Señor Arroz";
    }

    public async Task<EmailSendResult> SendPasswordResetEmailAsync(string toEmail, string userName, string resetToken, string resetUrl)
    {
        var subject = "Recuperación de Contraseña - SenorArroz";

        var resetLink = $"{resetUrl}?token={resetToken}&email={Uri.EscapeDataString(toEmail)}";
        var encodedResetLink = WebUtility.HtmlEncode(resetLink);
        var content = $@"
<h2 style='margin:0 0 18px;color:#171717;'>¡Hola {WebUtility.HtmlEncode(userName)}!</h2>
<p>Recibimos una solicitud para restablecer la contraseña de tu cuenta.</p>
<p style='text-align:center;margin:28px 0;'>
    <a href='{encodedResetLink}' style='display:inline-block;background:#f97316;color:#ffffff;padding:13px 24px;text-decoration:none;border-radius:7px;font-weight:bold;'>Restablecer contraseña</a>
</p>
<div style='background:#fff7ed;border-left:4px solid #f97316;padding:14px 16px;margin:20px 0;'>
    <strong>Importante:</strong> el enlace expira en una hora, solo puede usarse una vez y puedes ignorarlo si no solicitaste el cambio.
</div>
<p style='font-size:13px;color:#525252;'>Si el botón no funciona, copia este enlace:</p>
<p style='word-break:break-all;background:#f5f5f5;padding:12px;border-radius:5px;font-size:12px;'>{encodedResetLink}</p>";
        var body = BuildBrandedEmail("Recuperación de contraseña", "Protección de tu cuenta", content);

        return await QueueEmailAsync(
            messageType: "password_reset",
            toEmails: [toEmail],
            subject: subject,
            body: body,
            isHtml: true);
    }

    public async Task<EmailSendResult> SendPasswordResetConfirmationAsync(string toEmail, string userName)
    {
        var subject = "Contraseña Restablecida - SenorArroz";

        var changedAtColombia = ColombiaTimeHelper.GetNowInColombiaFromUtc(_clock.UtcNow);
        var content = $@"
<h2 style='margin:0 0 18px;color:#171717;'>¡Hola {WebUtility.HtmlEncode(userName)}!</h2>
<div style='background:#fff7ed;border-left:4px solid #f97316;padding:14px 16px;margin:18px 0;'>
    <strong>Tu contraseña fue restablecida correctamente.</strong>
</div>
<p>El cambio se realizó el <strong>{changedAtColombia:dd/MM/yyyy}</strong> a las <strong>{changedAtColombia:HH:mm}</strong>, hora de Colombia.</p>
<p>Si no realizaste este cambio, contacta inmediatamente al administrador del sistema.</p>
<p>Ya puedes iniciar sesión con tu nueva contraseña.</p>";
        var body = BuildBrandedEmail("Contraseña restablecida", "Confirmación de seguridad", content);

        return await QueueEmailAsync(
            messageType: "password_reset_confirmation",
            toEmails: [toEmail],
            subject: subject,
            body: body,
            isHtml: true);
    }

    public async Task<EmailSendResult> SendTestEmailAsync(string toEmail, string subject, string body)
    {
        var content = $"<p>{WebUtility.HtmlEncode(body).Replace("\r\n", "<br>").Replace("\n", "<br>")}</p>";
        return await QueueEmailAsync(
            messageType: "test",
            toEmails: [toEmail],
            subject: subject,
            body: BuildBrandedEmail("Correo de prueba", "Verificación del servicio de correo", content),
            isHtml: true);
    }

    public async Task<EmailSendResult> SendDailyMonetaryAuditEmailAsync(
        IReadOnlyCollection<string> toEmails,
        DailyMonetaryAuditEmailPayload payload,
        string? relatedEntityType = null,
        int? relatedEntityId = null)
    {
        if (toEmails.Count == 0)
            return EmailSendResult.Fail("none", "No hay destinatarios configurados para la auditoría monetaria.");

        var subject = $"Auditoría diaria - {payload.BranchName} - {payload.BusinessDate:yyyy-MM-dd}";
        var groupsHtml = string.Join("", payload.Groups.Select(group =>
            $@"<div style='margin:0 0 18px;border:1px solid #e5e5e5;border-top:4px solid #f97316;border-radius:7px;padding:16px;'>
<h3 style='margin:0 0 8px;color:#171717;'>{WebUtility.HtmlEncode(group.Title)}</h3>
<p style='margin:0 0 10px;color:#525252;'>Eventos: <strong>{group.EventCount}</strong>{(group.NetDifference < 0 ? $" · Reducción total: <strong>{FormatMoney(Math.Abs(group.NetDifference))}</strong>" : string.Empty)}</p>
<ul style='margin:0;padding-left:20px;'>{string.Join("", group.Lines.Select(line => $"<li style='margin-bottom:8px;'>{WebUtility.HtmlEncode(line)}</li>"))}</ul>
</div>"));
        var periodStartColombia = ColombiaTimeHelper.GetNowInColombiaFromUtc(payload.PeriodStartUtc);
        var periodEndColombia = ColombiaTimeHelper.GetNowInColombiaFromUtc(payload.PeriodEndUtc);
        var emptyState = payload.Groups.Count == 0
            ? "<div style='background:#fff7ed;border-left:4px solid #f97316;padding:16px;'>No hubo pedidos cancelados ni reducciones monetarias durante este periodo.</div>"
            : groupsHtml;
        var trackingGroupsHtml = string.Join("", payload.TrackingAlertGroups.Select(group =>
            $@"<tr>
<td style='padding:9px;border-bottom:1px solid #e5e5e5;'>{WebUtility.HtmlEncode(group.Title)}</td>
<td style='padding:9px;border-bottom:1px solid #e5e5e5;'>{WebUtility.HtmlEncode(group.Severity)}</td>
<td style='padding:9px;border-bottom:1px solid #e5e5e5;text-align:center;'>{group.EventCount}</td>
<td style='padding:9px;border-bottom:1px solid #e5e5e5;text-align:center;'>{group.ActiveCount}</td>
</tr>
<tr><td colspan='4' style='padding:10px 14px 16px;border-bottom:2px solid #d4d4d4;background:#fafafa;'>
{BuildTrackingDetailsHtml(group.Details)}
</td></tr>"));
        var trackingSection = payload.TrackingAlertGroups.Count == 0
            ? "<div style='background:#ecfdf5;border-left:4px solid #10b981;padding:16px;'>No se generaron alertas delicadas de seguimiento incluidas en la auditoría durante este periodo.</div>"
            : $@"<table style='width:100%;border-collapse:collapse;border:1px solid #e5e5e5;'>
<thead><tr style='background:#f5f5f5;'><th style='padding:9px;text-align:left;'>Tipo</th><th style='padding:9px;text-align:left;'>Nivel</th><th style='padding:9px;'>Eventos</th><th style='padding:9px;'>Activas</th></tr></thead>
<tbody>{trackingGroupsHtml}</tbody></table>";
        var content = $@"
<p style='margin-top:0;'>Sucursal: <strong>{WebUtility.HtmlEncode(payload.BranchName)}</strong></p>
<p>Fecha de negocio: <strong>{payload.BusinessDate:dd/MM/yyyy}</strong></p>
<p style='color:#525252;'>Periodo auditado (hora de Colombia): {periodStartColombia:dd/MM/yyyy HH:mm} a {periodEndColombia:dd/MM/yyyy HH:mm}</p>
<h2 style='margin:24px 0 12px;color:#171717;'>Auditoría monetaria</h2>
{emptyState}
<h2 style='margin:24px 0 12px;color:#171717;'>Alertas de seguimiento de domiciliarios</h2>
{trackingSection}";
        var body = BuildBrandedEmail("Auditoría diaria", "Operación monetaria y seguimiento de domiciliarios", content);

        return await QueueEmailAsync(
            messageType: "daily_monetary_audit",
            toEmails: toEmails,
            subject: subject,
            body: body,
            isHtml: true,
            relatedEntityType: relatedEntityType,
            relatedEntityId: relatedEntityId);
    }

    private async Task<EmailSendResult> QueueEmailAsync(
        string messageType,
        IReadOnlyCollection<string> toEmails,
        string subject,
        string body,
        bool isHtml,
        string? relatedEntityType = null,
        int? relatedEntityId = null,
        string? metadataJson = null)
    {
        try
        {
            var recipients = toEmails
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (recipients.Count == 0)
                return EmailSendResult.Fail("outbox", "No hay destinatarios configurados.");

            _context.EmailOutboxMessages.Add(new EmailOutboxMessage
            {
                MessageType = messageType,
                ToEmailsJson = JsonSerializer.Serialize(recipients),
                Subject = subject,
                Body = body,
                IsHtml = isHtml,
                Status = "pending",
                AttemptCount = 0,
                MaxAttempts = _maxAttempts,
                NextAttemptAt = _clock.UtcNow,
                RelatedEntityType = relatedEntityType,
                RelatedEntityId = relatedEntityId,
                MetadataJson = string.IsNullOrWhiteSpace(metadataJson) ? "{}" : metadataJson
            });

            await _context.SaveChangesAsync();
            _workSignal?.Pulse();
            _logger.LogInformation("Email queued successfully. Type: {MessageType}. Recipients: {Recipients}", messageType, string.Join(", ", recipients));
            return EmailSendResult.Ok("outbox");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to queue email. Type: {MessageType}", messageType);
            return EmailSendResult.Fail("outbox", ex.Message);
        }
    }

    private string BuildBrandedEmail(string title, string subtitle, string contentHtml)
    {
        var encodedTitle = WebUtility.HtmlEncode(title);
        var encodedSubtitle = WebUtility.HtmlEncode(subtitle);
        var encodedRestaurantName = WebUtility.HtmlEncode(_restaurantName);
        var encodedLogoUrl = WebUtility.HtmlEncode(_logoUrl);
        var currentYear = ColombiaTimeHelper.GetNowInColombiaFromUtc(_clock.UtcNow).Year;

        return $@"<!DOCTYPE html>
<html>
<head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>{encodedTitle}</title></head>
<body style='margin:0;padding:0;background:#f5f5f5;font-family:Arial,sans-serif;color:#262626;line-height:1.55;'>
<table role='presentation' width='100%' cellspacing='0' cellpadding='0' style='background:#f5f5f5;padding:24px 10px;'>
<tr><td align='center'>
<table role='presentation' width='100%' cellspacing='0' cellpadding='0' style='max-width:680px;background:#ffffff;border-radius:10px;overflow:hidden;border:1px solid #e5e5e5;'>
<tr><td style='background:#111111;border-bottom:6px solid #f97316;padding:22px 28px;text-align:center;'>
    <img src='{encodedLogoUrl}' width='92' alt='{encodedRestaurantName}' style='display:block;margin:0 auto 10px;max-width:92px;height:auto;'>
    <h1 style='margin:0;color:#ffffff;font-size:25px;'>{encodedTitle}</h1>
    <p style='margin:5px 0 0;color:#fdba74;font-size:14px;'>{encodedSubtitle}</p>
</td></tr>
<tr><td style='padding:28px;'>{contentHtml}</td></tr>
<tr><td style='background:#171717;color:#d4d4d4;padding:18px 28px;text-align:center;font-size:12px;'>
    <div style='color:#fb923c;font-weight:bold;margin-bottom:4px;'>{encodedRestaurantName}</div>
    <div>© {currentYear}. Correo automático, por favor no responder.</div>
</td></tr>
</table>
</td></tr>
</table>
</body>
</html>";
    }

    private static string FormatMoney(decimal value) => $"${value.ToString("N0", ColombianCulture)}";

    private static string BuildTrackingDetailsHtml(IReadOnlyCollection<DailyTrackingAlertEmailDetail> details)
    {
        if (details.Count == 0)
            return "<span style='color:#737373;'>Sin detalle adicional.</span>";

        return $"<ul style='margin:0;padding-left:20px;'>{string.Join("", details.Select(detail =>
        {
            var occurredAt = ColombiaTimeHelper.GetNowInColombiaFromUtc(detail.OccurredAt);
            var endedAt = detail.EndedAt.HasValue
                ? ColombiaTimeHelper.GetNowInColombiaFromUtc(detail.EndedAt.Value)
                : (DateTime?)null;
            var timeSummary = endedAt.HasValue
                ? $"Inicio: {occurredAt:dd/MM/yyyy HH:mm:ss} · Fin: {endedAt:dd/MM/yyyy HH:mm:ss}"
                : $"Inicio: {occurredAt:dd/MM/yyyy HH:mm:ss} · Sin recuperación registrada";
            var duration = detail.DurationSeconds.HasValue
                ? $" · Duración: {FormatDuration(detail.DurationSeconds.Value)}"
                : string.Empty;
            var locations = string.Join(" · ", new[]
            {
                BuildMapLink(
                    detail.StartLocationLabel,
                    detail.StartLatitude,
                    detail.StartLongitude,
                    detail.StartLocationRecordedAt),
                BuildMapLink(
                    detail.EndLocationLabel,
                    detail.EndLatitude,
                    detail.EndLongitude,
                    detail.EndLocationRecordedAt),
            }.Where(x => !string.IsNullOrEmpty(x)));
            return $@"<li style='margin:0 0 12px;'>
<strong>{WebUtility.HtmlEncode(detail.DeliverymanName)}</strong><br>
<span>{WebUtility.HtmlEncode(timeSummary + duration)}</span><br>
<span style='color:#525252;'>{WebUtility.HtmlEncode(detail.Description)}</span>
{(string.IsNullOrEmpty(locations) ? string.Empty : $"<br>{locations}")}
</li>";
        }))}</ul>";
    }

    private static string BuildMapLink(
        string label,
        decimal? latitude,
        decimal? longitude,
        DateTime? recordedAt)
    {
        if (!latitude.HasValue || !longitude.HasValue)
            return string.Empty;
        var lat = latitude.Value.ToString("0.000000", CultureInfo.InvariantCulture);
        var lng = longitude.Value.ToString("0.000000", CultureInfo.InvariantCulture);
        var captured = recordedAt.HasValue
            ? $" ({ColombiaTimeHelper.GetNowInColombiaFromUtc(recordedAt.Value):HH:mm:ss})"
            : string.Empty;
        return $"<a href='https://www.google.com/maps?q={lat},{lng}' " +
            $"style='color:#047857;text-decoration:underline;' target='_blank'>" +
            $"{WebUtility.HtmlEncode(label + captured)}</a>";
    }

    private static string FormatDuration(int totalSeconds)
    {
        var duration = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours} h {duration.Minutes} min {duration.Seconds} s";
        if (duration.TotalMinutes >= 1)
            return $"{duration.Minutes} min {duration.Seconds} s";
        return $"{duration.Seconds} s";
    }
}
