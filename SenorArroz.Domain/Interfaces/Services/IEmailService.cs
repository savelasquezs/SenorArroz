// SenorArroz.Domain/Interfaces/Services/IEmailService.cs
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SenorArroz.Domain.Models;
using System.Net;
using System.Net.Mail;

namespace SenorArroz.Domain.Interfaces.Services;

public interface IEmailService
{
    Task<bool> SendPasswordResetEmailAsync(string toEmail, string userName, string resetToken, string resetUrl);
    Task<bool> SendPasswordResetConfirmationAsync(string toEmail, string userName);
    Task<bool> SendTestEmailAsync(string toEmail, string subject, string body);
    Task<bool> SendDailyMonetaryAuditEmailAsync(IReadOnlyCollection<string> toEmails, DailyMonetaryAuditEmailPayload payload);
}
