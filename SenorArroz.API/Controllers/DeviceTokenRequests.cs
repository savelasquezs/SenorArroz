using System.ComponentModel.DataAnnotations;

namespace SenorArroz.API.Controllers;

public record RegisterDeviceTokenRequest(string Token, string? Platform);
public record RemoveDeviceTokenRequest(string Token);
public record RecordLocationRequest(int WorkSessionId, decimal Latitude, decimal Longitude, DateTime RecordedAt);

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
