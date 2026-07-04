namespace SenorArroz.Application.Common.Interfaces;

public record WhatsAppCloudTestResult(bool Success, string? DisplayPhoneNumber, string? ErrorMessage);

public record WhatsAppCloudSendResult(bool Success, string? WhatsAppMessageId, string? ErrorMessage);

public interface IWhatsAppCloudClient
{
    Task<WhatsAppCloudTestResult> TestConnectionAsync(
        string phoneNumberId,
        string accessToken,
        CancellationToken cancellationToken = default);

    Task<WhatsAppCloudSendResult> SendTextMessageAsync(
        string phoneNumberId,
        string accessToken,
        string toPhoneNumber,
        string text,
        CancellationToken cancellationToken = default);
}
