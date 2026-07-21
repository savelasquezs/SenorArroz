using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;

namespace SenorArroz.Infrastructure.Services;

/// <summary>
/// Google Routes API v2 computeRoutes (solo distanceMeters + duration).
/// </summary>
public class GoogleRoutesDrivingMetricsService : IGoogleRoutesDrivingMetricsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly GoogleMapsRouteOptions _opts;
    private readonly ILogger<GoogleRoutesDrivingMetricsService> _logger;

    public GoogleRoutesDrivingMetricsService(
        HttpClient http,
        IOptions<GoogleMapsRouteOptions> opts,
        ILogger<GoogleRoutesDrivingMetricsService> logger)
    {
        _http = http;
        _opts = opts.Value;
        _logger = logger;
        if (_http.BaseAddress is null)
            _http.BaseAddress = new Uri("https://routes.googleapis.com/");
    }

    public async Task<DrivingRouteMetrics> ComputeRouteAsync(
        IReadOnlyList<(double Latitude, double Longitude)> orderedWaypoints,
        CancellationToken cancellationToken = default)
    {
        if (orderedWaypoints.Count < 2)
            return default;

        if (string.IsNullOrWhiteSpace(_opts.RoutesApiKey))
        {
            _logger.LogDebug("GoogleMaps:RoutesApiKey vacío; métricas de manejo en 0.");
            return default;
        }

        var origin = orderedWaypoints[0];
        var destination = orderedWaypoints[^1];
        RoutesWaypoint[]? intermediates = null;
        if (orderedWaypoints.Count > 2)
        {
            intermediates = orderedWaypoints
                .Skip(1)
                .Take(orderedWaypoints.Count - 2)
                .Select(p => new RoutesWaypoint
                {
                    Location = new RoutesLocation
                    {
                        LatLng = new RoutesLatLng { Latitude = p.Latitude, Longitude = p.Longitude },
                    },
                })
                .ToArray();
        }

        var body = new RoutesComputeRequest
        {
            Origin = new RoutesWaypoint
            {
                Location = new RoutesLocation
                {
                    LatLng = new RoutesLatLng { Latitude = origin.Latitude, Longitude = origin.Longitude },
                },
            },
            Destination = new RoutesWaypoint
            {
                Location = new RoutesLocation
                {
                    LatLng = new RoutesLatLng { Latitude = destination.Latitude, Longitude = destination.Longitude },
                },
            },
            Intermediates = intermediates,
            TravelMode = "DRIVE",
            RoutingPreference = "TRAFFIC_AWARE_OPTIMAL",
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "directions/v2:computeRoutes");
        request.Headers.TryAddWithoutValidation("X-Goog-Api-Key", _opts.RoutesApiKey);
        request.Headers.TryAddWithoutValidation(
            "X-Goog-FieldMask",
            "routes.distanceMeters,routes.duration,routes.legs.distanceMeters,routes.legs.duration");
        request.Content = JsonContent.Create(body, options: JsonOptions);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fallo de red al llamar Routes API.");
            return default;
        }

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Routes API {Status}: {Body}", response.StatusCode, err);
            return default;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!doc.RootElement.TryGetProperty("routes", out var routes) || routes.GetArrayLength() == 0)
            return default;

        var route0 = routes[0];
        var distance = ReadDistanceMeters(route0);
        var durationSec = ReadDurationSeconds(route0);
        var returnDistance = 0;
        var returnDuration = 0;
        if (route0.TryGetProperty("legs", out var legs) && legs.GetArrayLength() > 0)
        {
            var returnLeg = legs[legs.GetArrayLength() - 1];
            returnDistance = ReadDistanceMeters(returnLeg);
            returnDuration = ReadDurationSeconds(returnLeg);
        }
        return new DrivingRouteMetrics(distance, durationSec, returnDistance, returnDuration);
    }

    private sealed class RoutesComputeRequest
    {
        public RoutesWaypoint Origin { get; set; } = null!;
        public RoutesWaypoint Destination { get; set; } = null!;
        public RoutesWaypoint[]? Intermediates { get; set; }
        public string TravelMode { get; set; } = "DRIVE";
        public string RoutingPreference { get; set; } = "TRAFFIC_AWARE_OPTIMAL";
    }

    private sealed class RoutesWaypoint
    {
        public RoutesLocation Location { get; set; } = null!;
    }

    private sealed class RoutesLocation
    {
        public RoutesLatLng LatLng { get; set; } = null!;
    }

    private sealed class RoutesLatLng
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }

    private static int ReadDistanceMeters(JsonElement routeEl)
    {
        if (!routeEl.TryGetProperty("distanceMeters", out var d))
            return 0;
        return d.ValueKind == JsonValueKind.String
            ? int.TryParse(d.GetString(), out var v) ? v : 0
            : d.GetInt32();
    }

    private static int ReadDurationSeconds(JsonElement routeEl)
    {
        if (!routeEl.TryGetProperty("duration", out var dur))
            return 0;

        if (dur.ValueKind == JsonValueKind.String)
        {
            var s = dur.GetString() ?? "";
            return decimal.TryParse(
                s.TrimEnd('s'),
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture,
                out var sec)
                ? (int)Math.Ceiling(sec)
                : 0;
        }

        if (dur.TryGetProperty("seconds", out var secEl))
        {
            return secEl.ValueKind == JsonValueKind.String
                ? int.TryParse(secEl.GetString(), out var x) ? x : 0
                : secEl.GetInt32();
        }

        return 0;
    }
}
