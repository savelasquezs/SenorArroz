using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Application.Common.Services;

public sealed class DeliveryAppVersionPolicy(
    IOptions<DeliveryAppVersionOptions> options) : IDeliveryAppVersionPolicy
{
    private readonly DeliveryAppVersionOptions _options = options.Value;

    public DeliveryAppVersionEvaluation Evaluate(DeliveryAppClientVersion? clientVersion)
    {
        var compatible = !_options.Enabled
            || clientVersion is not null
            && string.Equals(
                clientVersion.VersionName?.Trim(),
                _options.RequiredVersionName.Trim(),
                StringComparison.Ordinal)
            && clientVersion.BuildNumber >= _options.MinimumBuildNumber
            && string.Equals(
                clientVersion.PackageName?.Trim(),
                DeliveryAppVersionOptions.RequiredPackageName,
                StringComparison.Ordinal);

        return new DeliveryAppVersionEvaluation(
            _options.RequiredVersionName,
            _options.MinimumBuildNumber,
            _options.PlayStoreUrl,
            !compatible);
    }

    public void EnsureCompatible(DeliveryAppClientVersion? clientVersion)
    {
        var evaluation = Evaluate(clientVersion);
        if (evaluation.UpdateRequired)
        {
            throw new DeliveryAppUpdateRequiredException(
                evaluation.RequiredVersion,
                evaluation.MinimumBuild,
                evaluation.PlayStoreUrl);
        }
    }
}
