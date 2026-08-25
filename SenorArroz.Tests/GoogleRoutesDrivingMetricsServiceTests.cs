using System.Net;
using System.Text;
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
    }

    private sealed class RoutesHandler : HttpMessageHandler
    {
        public string FieldMask { get; private set; } = string.Empty;
        public string RequestUri { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            FieldMask = Assert.Single(request.Headers.GetValues("X-Goog-FieldMask"));
            RequestUri = request.RequestUri!.ToString();
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
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }
}
