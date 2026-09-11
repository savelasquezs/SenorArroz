using SenorArroz.Application.Common.Models;
namespace SenorArroz.Application.Common.Interfaces;

public record WhatsAppCloudTestResult(bool Success, string? DisplayPhoneNumber, string? ErrorMessage);

public record WhatsAppCloudSendResult(bool Success, string? WhatsAppMessageId, string? ErrorMessage);

public record WhatsAppCloudTemplate(
    string MetaTemplateId,
    string Name,
    string Language,
    string Category,
    string Status,
    string ComponentsJson);

public record WhatsAppCloudTemplateSyncResult(
    bool Success,
    IReadOnlyList<WhatsAppCloudTemplate> Templates,
    string? ErrorMessage);

public record WhatsAppCloudUploadMediaResult(bool Success, string? MediaId, string? ErrorMessage);

public record WhatsAppCloudMediaInfoResult(
    bool Success,
    string? MediaId,
    string? DownloadUrl,
    string? MimeType,
    string? Sha256,
    long? FileSize,
    string? ErrorMessage);

public record WhatsAppCloudDownloadedMedia(
    bool Success,
    byte[]? Content,
    string? ContentType,
    string? ErrorMessage);

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
    Task<WhatsAppCloudSendResult> SendUrlButtonMessageAsync(string phoneNumberId, string accessToken, string toPhoneNumber, string body, string buttonText, string url, CancellationToken cancellationToken = default);
    Task<WhatsAppCloudSendResult> SendReplyButtonsMessageAsync(string phoneNumberId,string accessToken,string toPhoneNumber,string body,IReadOnlyList<WhatsAppReplyButton> buttons,CancellationToken cancellationToken=default);
    Task<WhatsAppCloudSendResult> SendImageLinkMessageAsync(string phoneNumberId, string accessToken, string toPhoneNumber, string imageUrl, string? caption, CancellationToken cancellationToken = default);
    Task<WhatsAppCloudSendResult> SendFlowMessageAsync(string phoneNumberId,string accessToken,string toPhoneNumber,string body,string buttonText,string flowId,string flowToken,string initialScreen,CancellationToken cancellationToken=default);

    Task<WhatsAppCloudTemplateSyncResult> GetMessageTemplatesAsync(
        string businessAccountId,
        string accessToken,
        CancellationToken cancellationToken = default);

    Task<WhatsAppCloudSendResult> SendTemplateMessageAsync(
        string phoneNumberId,
        string accessToken,
        string toPhoneNumber,
        string templateName,
        string language,
        IReadOnlyList<string> parameters,
        CancellationToken cancellationToken = default);

    Task<WhatsAppCloudSendResult> SendAuthenticationTemplateMessageAsync(
        string phoneNumberId,
        string accessToken,
        string toPhoneNumber,
        string templateName,
        string language,
        string code,
        CancellationToken cancellationToken = default);

    Task<WhatsAppCloudUploadMediaResult> UploadMediaAsync(
        string phoneNumberId,
        string accessToken,
        byte[] content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default);

    Task<WhatsAppCloudSendResult> SendMediaMessageAsync(
        string phoneNumberId,
        string accessToken,
        string toPhoneNumber,
        string mediaType,
        string mediaId,
        string? caption,
        string? fileName,
        CancellationToken cancellationToken = default);

    Task<WhatsAppCloudMediaInfoResult> GetMediaInfoAsync(
        string mediaId,
        string accessToken,
        CancellationToken cancellationToken = default);

    Task<WhatsAppCloudDownloadedMedia> DownloadMediaAsync(
        string downloadUrl,
        string accessToken,
        CancellationToken cancellationToken = default);
}
