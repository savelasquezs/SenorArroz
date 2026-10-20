using System.Net;
using System.Net.Mail;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.EmployeeLeaves;

public static class EmployeeLeaveEmail
{
    public static async Task EnqueueAsync(IApplicationDbContext db, IConfiguration configuration,
        EmployeeLeaveRequest leave, int actorId, string operation, DateTime now, CancellationToken ct)
    {
        var reviewerIds = await EmployeeLeaveQueries.Reviewers(db, leave).Select(x => x.Id).ToListAsync(ct);
        var users = await db.Users.AsNoTracking().Where(x => x.Active && x.AccountType == EmployeeAccountType.Personal
            && (reviewerIds.Contains(x.Id) || x.Id == leave.UserId)).ToListAsync(ct);
        var baseUrl = configuration["EmployeeLeaves:WebBaseUrl"]?.Trim().TrimEnd('/');
        var hasLink = Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.IsLoopback);
        var link = hasLink ? $"{baseUrl}/permisos/{leave.PublicId:D}" : null;
        var status = leave.Status switch
        {
            "approved" => "Aprobado", "denied" => "Denegado", "postponed" => "Aplazado",
            "cancelled" => "Cancelado", _ => "Pendiente"
        };
        foreach (var recipient in users.GroupBy(x => x.Email.Trim(), StringComparer.OrdinalIgnoreCase).Select(x => x.First()))
        {
            // El autor no recibe correos por cada soporte propio; sí recibe la constancia inicial.
            if (recipient.Id == actorId && operation != "created") continue;
            var validEmail = MailAddress.TryCreate(recipient.Email.Trim(), out _);
            var reason = recipient.Id == leave.UserId && !string.IsNullOrWhiteSpace(leave.DecisionReason)
                ? $"<p><strong>Motivo:</strong> {WebUtility.HtmlEncode(leave.DecisionReason)}</p>" : "";
            var body = $"<h2>Permisos laborales</h2><p>Solicitud #{leave.Id} — {status}</p>"
                + $"<p>Trabajador: {WebUtility.HtmlEncode(leave.ApplicantName)}</p>"
                + $"<p>Desde {leave.StartsAt.AddHours(-5):dd/MM/yyyy HH:mm} hasta {leave.EndsAt.AddHours(-5):dd/MM/yyyy HH:mm}, hora de Colombia.</p>"
                + reason + (link is null ? "<p>Consulta el detalle en Permisos, dentro de la aplicación.</p>"
                    : $"<p><a href='{WebUtility.HtmlEncode(link)}'>Ver solicitud</a> — requiere iniciar sesión.</p>")
                + "<p>No se incluyen soportes ni justificaciones personales en este correo.</p>";
            db.EmailOutboxMessages.Add(new EmailOutboxMessage
            {
                TenantId = leave.TenantId, MessageType = "employee_leave_changed",
                ToEmailsJson = JsonSerializer.Serialize(new[] { recipient.Email.Trim() }),
                Subject = $"Permiso #{leave.Id}: {status}", Body = body, IsHtml = true,
                Status = validEmail ? "pending" : "failed", LastError = validEmail ? null : "El correo registrado no es válido.",
                NextAttemptAt = now, RelatedEntityType = "employee_leave_request", RelatedEntityId = leave.Id,
                MetadataJson = JsonSerializer.Serialize(new { leave.PublicId, leave.Version, recipientUserId = recipient.Id, operation }),
                CreatedAt = now, UpdatedAt = now
            });
        }
    }
}
