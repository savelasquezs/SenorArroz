using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Options;
using SenorArroz.Infrastructure.Services;

namespace SenorArroz.Tests;

public class GoogleRoutesDrivingMetricsServiceTests
{
    [Fact]
    public async Task ComputeRouteAsync_RequestsAndParsesEncodedPolyline()
    {
        var handler = new RoutesHandler();
        var service = new GoogleRoutesDrivingMetricsService(
            new HttpClient(handler),
            Options.Create(new GoogleMapsRouteOptions { RoutesApiKey = "test" }),
            NullLogger<GoogleRoutesDrivingMetricsService>.Instance);

        var metrics = await service.ComputeRouteAsync([(6.30, -75.57), (6.25, -75.56)]);

        Assert.Equal(3_210, metrics.DistanceMeters);
        Assert.Equal(766, metrics.DurationSeconds);
        Assert.Equal(3_210, metrics.ReturnDistanceMeters);
        Assert.Equal(766, metrics.ReturnDurationSeconds);
        Assert.Equal("encoded-route", metrics.EncodedPolyline);
        Assert.Contains("routes.polyline.encodedPolyline", handler.FieldMask);
        Assert.Equal("https://routes.googleapis.com/directions/v2:computeRoutes", handler.RequestUri);
        Assert.Equal("TRAFFIC_UNAWARE", Assert.Single(handler.RoutingPreferences));
        Assert.Equal(2, Assert.Single(handler.WaypointsPerRequest));
    }

    [Fact]
    public async Task ComputeTrafficAwareRouteAsync_RequestsProOnlyWhenExplicit()
    {
        var handler = new RoutesHandler();
        var service = new GoogleRoutesDrivingMetricsService(
            new HttpClient(handler),
            Options.Create(new GoogleMapsRouteOptions { RoutesApiKey = "test" }),
            NullLogger<GoogleRoutesDrivingMetricsService>.Instance);

        var metrics = await service.ComputeTrafficAwareRouteAsync([(6.30, -75.57), (6.25, -75.56)]);

        Assert.Equal(3_210, metrics.DistanceMeters);
        Assert.Equal("TRAFFIC_AWARE_OPTIMAL", Assert.Single(handler.RoutingPreferences));
    }

    [Fact]
    public async Task ComputeRouteAsync_SplitsLongRouteIntoEssentialsChunks()
    {
        var handler = new RoutesHandler();
        var service = new GoogleRoutesDrivingMetricsService(
            new HttpClient(handler),
            Options.Create(new GoogleMapsRouteOptions { RoutesApiKey = "test" }),
            NullLogger<GoogleRoutesDrivingMetricsService>.Instance);
        var points = Enumerable.Range(0, 13)
            .Select(i => (Latitude: 6.30 + i * 0.001, Longitude: -75.57))
            .ToArray();

        var metrics = await service.ComputeRouteAsync(points);

        Assert.Equal(new[] { 12, 2 }, handler.WaypointsPerRequest);
        Assert.Equal(new[] { "TRAFFIC_UNAWARE", "TRAFFIC_UNAWARE" }, handler.RoutingPreferences);
        Assert.Equal(6_420, metrics.DistanceMeters);
        Assert.Equal(1_532, metrics.DurationSeconds);
        Assert.Equal(3_210, metrics.ReturnDistanceMeters);
        Assert.Equal(766, metrics.ReturnDurationSeconds);
        Assert.Null(metrics.EncodedPolyline);
    }

    [Fact]
    public async Task ComputeRouteAsync_DoesNotCallGoogleWithoutKey()
    {
        var handler = new RoutesHandler();
        var service = new GoogleRoutesDrivingMetricsService(
            new HttpClient(handler),
            Options.Create(new GoogleMapsRouteOptions { RoutesApiKey = "" }),
            NullLogger<GoogleRoutesDrivingMetricsService>.Instance);

        var metrics = await service.ComputeRouteAsync([(6.30, -75.57), (6.25, -75.56)]);

        Assert.Equal(default, metrics);
        Assert.Empty(handler.RoutingPreferences);
    }

    [Fact]
    public async Task ComputeRoutes_RecordsEverySendAttemptWithOnlyRoutingPreference()
    {
        var observed = new List<(long Count, string Preference)>();
        // MeterListener is process-wide; capture only this test's async execution context.
        var capture = new AsyncLocal<bool>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "SenorArroz.GoogleRoutes"
                && instrument.Name == "senorarroz.google.routes.http.attempts")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, count, tags, _) =>
        {
            if (!capture.Value) return;
            Assert.Equal(1, tags.Length);
            Assert.Equal("routing.preference", tags[0].Key);
            observed.Add((count, Assert.IsType<string>(tags[0].Value)));
        });
        listener.Start();
        var handler = new RoutesHandler();
        using var http = new HttpClient(handler);
        var service = new GoogleRoutesDrivingMetricsService(http,
            Options.Create(new GoogleMapsRouteOptions { RoutesApiKey = "test" }),
            NullLogger<GoogleRoutesDrivingMetricsService>.Instance);
        var withoutKey = new GoogleRoutesDrivingMetricsService(http,
            Options.Create(new GoogleMapsRouteOptions { RoutesApiKey = "" }),
            NullLogger<GoogleRoutesDrivingMetricsService>.Instance);
        var points = Enumerable.Range(0, 13)
            .Select(i => (Latitude: 6.30 + i * 0.001, Longitude: -75.57))
            .ToArray();

        capture.Value = true;
        await service.ComputeRouteAsync(points);
        await service.ComputeTrafficAwareRouteAsync(points.Take(2).ToArray());
        await withoutKey.ComputeRouteAsync(points);
        await service.ComputeRouteAsync(points.Take(1).ToArray());
        handler.StatusCode = HttpStatusCode.TooManyRequests;
        await service.ComputeRouteAsync(points.Take(2).ToArray());
        capture.Value = false;

        Assert.Equal(new[] { 12, 2, 2, 2 }, handler.WaypointsPerRequest);
        Assert.Equal(handler.RoutingPreferences.ToArray(), observed.Select(x => x.Preference).ToArray());
        Assert.Equal(4, observed.Count);
        Assert.All(observed, x => Assert.Equal(1L, x.Count));
    }

    private sealed class RoutesHandler : HttpMessageHandler
    {
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
        public string FieldMask { get; private set; } = string.Empty;
        public string RequestUri { get; private set; } = string.Empty;
        public List<string> RoutingPreferences { get; } = [];
        public List<int> WaypointsPerRequest { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            FieldMask = Assert.Single(request.Headers.GetValues("X-Goog-FieldMask"));
            RequestUri = request.RequestUri!.ToString();
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            RoutingPreferences.Add(body.RootElement.GetProperty("routingPreference").GetString()!);
            var intermediates = body.RootElement.TryGetProperty("intermediates", out var values) ? values.GetArrayLength() : 0;
            WaypointsPerRequest.Add(2 + intermediates);
            const string json = """
                {
                  "routes": [{
                    "distanceMeters": 3210,
                    "duration": "765.2s",
                    "polyline": { "encodedPolyline": "encoded-route" },
                    "legs": [{ "distanceMeters": 3210, "duration": "765.2s" }]
                  }]
                }
                """;
            return new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }
    }
}
