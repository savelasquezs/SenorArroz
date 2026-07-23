namespace SenorArroz.Domain.Exceptions;

public sealed class SessionReplacedException : UnauthorizedAccessException
{
    public const string ErrorCode = "SESSION_REPLACED";
    public const string DefaultMessage =
        "Tu sesión fue iniciada en otro dispositivo. Inicia sesión nuevamente.";

    public SessionReplacedException() : base(DefaultMessage)
    {
    }
}
