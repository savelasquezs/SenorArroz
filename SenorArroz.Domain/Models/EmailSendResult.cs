namespace SenorArroz.Domain.Models;

public class EmailSendResult
{
    public bool Success { get; init; }
    public string Provider { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }
    public string? MessageId { get; init; }
    public bool PermanentFailure { get; init; }

    public static EmailSendResult Ok(string provider, string? messageId = null) => new()
    {
        Success = true,
        Provider = provider,
        MessageId = messageId
    };

    public static EmailSendResult Fail(string provider, string? errorMessage, bool permanent = false) => new()
    {
        Success = false,
        Provider = provider,
        ErrorMessage = errorMessage,
        PermanentFailure = permanent
    };
}
