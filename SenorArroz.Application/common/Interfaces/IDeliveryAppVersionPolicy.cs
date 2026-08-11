namespace SenorArroz.Application.Common.Interfaces;

public sealed record DeliveryAppClientVersion(
    string? VersionName,
    int? BuildNumber,
    string? PackageName);

public sealed record DeliveryAppVersionEvaluation(
    string RequiredVersion,
    int MinimumBuild,
    string PlayStoreUrl,
    bool UpdateRequired);

public interface IDeliveryAppVersionPolicy
{
    DeliveryAppVersionEvaluation Evaluate(DeliveryAppClientVersion? clientVersion);
    void EnsureCompatible(DeliveryAppClientVersion? clientVersion);
}
