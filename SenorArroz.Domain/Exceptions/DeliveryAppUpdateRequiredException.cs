namespace SenorArroz.Domain.Exceptions;

public sealed class DeliveryAppUpdateRequiredException(
    string requiredVersion,
    int minimumBuild,
    string playStoreUrl)
    : Exception("Debes actualizar la aplicación para continuar.")
{
    public const string ErrorCode = "DELIVERY_APP_UPDATE_REQUIRED";

    public string RequiredVersion { get; } = requiredVersion;
    public int MinimumBuild { get; } = minimumBuild;
    public string PlayStoreUrl { get; } = playStoreUrl;
}
