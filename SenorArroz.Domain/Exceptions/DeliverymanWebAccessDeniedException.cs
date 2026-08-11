namespace SenorArroz.Domain.Exceptions;

public sealed class DeliverymanWebAccessDeniedException : UnauthorizedAccessException
{
    public const string ErrorCode = "DELIVERYMAN_WEB_ACCESS_DENIED";
    public const string DefaultMessage =
        "No tienes habilitado el acceso web como domiciliario.";

    public DeliverymanWebAccessDeniedException() : base(DefaultMessage)
    {
    }
}
