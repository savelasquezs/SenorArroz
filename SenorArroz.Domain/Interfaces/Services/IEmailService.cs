// SenorArroz.Domain/Interfaces/Services/IEmailService.cs
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SenorArroz.Domain.Models;
using System.Net;
using System.Net.Mail;

namespace SenorArroz.Domain.Interfaces.Services;

public interface IEmailService
{
    Task<EmailSendResult> SendPasswordResetEmailAsync(string toEmail, string userName, string resetToken, string resetUrl);
    Task<EmailSendResult> SendPasswordResetConfirmationAsync(string toEmail, string userName);
    Task<EmailSendResult> SendTestEmailAsync(string toEmail, string subject, string body);
    Task<EmailSendResult> SendPlatformOtpEmailAsync(string toEmail, string userName, string code, DateTime expiresAt);
    Task<EmailSendResult> SendTenantInvitationEmailAsync(string toEmail, string userName, string tenantName, string invitationUrl, DateTime expiresAt);
    Task<EmailSendResult> SendDailyMonetaryAuditEmailAsync(
        IReadOnlyCollection<string> toEmails,
        DailyMonetaryAuditEmailPayload payload,
        string? relatedEntityType = null,
        int? relatedEntityId = null);
}
