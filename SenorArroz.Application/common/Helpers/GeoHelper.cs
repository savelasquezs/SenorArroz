namespace SenorArroz.Application.Common.Helpers;

/// <summary>
/// Distancia sobre el elipsoide (aprox. esfera) entre dos puntos WGS84.
/// </summary>
public static class GeoHelper
{
    private const double EarthRadiusMeters = 6371000.0;

    public static double HaversineDistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        static double ToRad(double deg) => deg * (Math.PI / 180.0);

        var dLat = ToRad(lat2 - lat1);
        var dLon = ToRad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusMeters * c;
    }
}
