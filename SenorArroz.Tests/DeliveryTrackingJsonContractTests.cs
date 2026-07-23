using System.Text.Json;
using System.Text.Json.Serialization;
using SenorArroz.API.Controllers;
using SenorArroz.API.Extensions;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Tests;

public class DeliveryTrackingJsonContractTests
{
    [Theory]
    [InlineData("active_delivery")]
    [InlineData("ACTIVE_DELIVERY")]
    [InlineData("ActiveDelivery")]
    public void RecordLocationRequest_AcceptsTrackingModeUsedByMobileApp(string trackingMode)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(new SnakeCaseNamingPolicy()));

        var json = $$"""
            {
              "workSessionId": 8,
              "latitude": 6.2,
              "longitude": -75.5,
              "recordedAt": "2026-07-23T20:00:00Z",
              "trackingMode": "{{trackingMode}}"
            }
            """;

        var request = JsonSerializer.Deserialize<RecordLocationRequest>(json, options);

        Assert.NotNull(request);
        Assert.Equal(DeliveryTrackingMode.ActiveDelivery, request.TrackingMode);
    }

    [Fact]
    public void RecordLocationRequest_WritesCanonicalTrackingMode()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter(new SnakeCaseNamingPolicy()));
        var request = new RecordLocationRequest(
            8,
            6.2m,
            -75.5m,
            DateTime.UtcNow,
            TrackingMode: DeliveryTrackingMode.ActiveDelivery);

        var json = JsonSerializer.Serialize(request, options);

        Assert.Contains("\"TrackingMode\":\"active_delivery\"", json);
    }
}
