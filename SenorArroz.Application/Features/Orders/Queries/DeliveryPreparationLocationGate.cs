using SenorArroz.Application.Common.Helpers;

namespace SenorArroz.Application.Features.Orders.Queries;

public static class DeliveryPreparationLocationGate
{
    public static bool IsInside(
        decimal latitude,
        decimal longitude,
        decimal branchLatitude,
        decimal branchLongitude,
        int allowedDistanceMeters,
        out double distanceMeters)
    {
        distanceMeters = GeoHelper.HaversineDistanceMeters(
            (double)latitude,
            (double)longitude,
            (double)branchLatitude,
            (double)branchLongitude);

        return distanceMeters <= Math.Max(1, allowedDistanceMeters);
    }
}
