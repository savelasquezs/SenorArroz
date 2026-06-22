namespace SenorArroz.Domain.Models;

public class EmailSendResult
{
    public bool Success { get; init; }
    public string Provider { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }

    public static EmailSendResult Ok(string provider) => new()
    {
        Success = true,
        Provider = provider
    };

    public static EmailSendResult Fail(string provider, string? errorMessage) => new()
    {
        Success = false,
        Provider = provider,
        ErrorMessage = errorMessage
    };
}
