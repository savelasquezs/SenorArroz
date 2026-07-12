namespace SenorArroz.Application.Options;

public class GoogleMapsRouteOptions
{
    public const string SectionName = "GoogleMaps";

    /// <summary>API key con Routes API habilitada. Si está vacía, distancia y tiempo de manejo quedan en 0.</summary>
    public string? RoutesApiKey { get; set; }
    /// <summary>API key con Geocoding API habilitada. Puede configurarse mediante GoogleMaps__GeocodingApiKey.</summary>
    public string? GeocodingApiKey { get; set; }
}
