using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using SenorArroz.API.Extensions;
using SenorArroz.Domain.Enums;

namespace SenorArroz.API.Controllers;

public record RegisterDeviceTokenRequest(string Token, string? Platform);
public record RemoveDeviceTokenRequest(string Token);
public record RecordLocationRequest(
    int WorkSessionId,
    decimal Latitude,
    decimal Longitude,
    DateTime RecordedAt,
    Guid? ClientPointId = null,
    int? DeliveryRouteId = null,
    double? AccuracyMeters = null,
    double? HeadingDegrees = null,
    int? BatteryLevelPercent = null,
    bool? InternetAvailable = null,
    bool? GpsEnabled = null,
    [property: JsonConverter(typeof(DeliveryTrackingModeJsonConverter))]
    DeliveryTrackingMode? TrackingMode = null);

public record RecordDeliveryDeviceEventRequest(
    int WorkSessionId,
    DeliveryDeviceEventType EventType,
    DateTime RecordedAt,
    Guid? ClientEventId = null,
    int? BatteryLevelPercent = null,
    bool? InternetAvailable = null,
    bool? GpsEnabled = null,
    bool? LocationPermissionGranted = null,
    string? Details = null,
    int? OfflineLocationCount = null,
    DateTime? OfflineStartedAt = null,
    DateTime? OfflineEndedAt = null);

public class StartDeliveryWorkSessionRequest
{
    [Required, StringLength(64)]
    public string DeviceInstallationId { get; set; } = string.Empty;

    [Required, StringLength(30)]
    public string DevicePlatform { get; set; } = string.Empty;

    [StringLength(300)]
    public string? DeviceDescription { get; set; }

    [StringLength(40)]
    public string? AppVersion { get; set; }
}
