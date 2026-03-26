namespace SenorArroz.Application.Options;

public class GoogleMapsRouteOptions
{
    public const string SectionName = "GoogleMaps";

    /// <summary>API key con Routes API habilitada. Si está vacía, distancia y tiempo de manejo quedan en 0.</summary>
    public string? RoutesApiKey { get; set; }
}
