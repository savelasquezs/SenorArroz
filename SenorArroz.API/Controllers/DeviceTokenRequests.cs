namespace SenorArroz.API.Controllers;

public record RegisterDeviceTokenRequest(string Token, string? Platform);
public record RemoveDeviceTokenRequest(string Token);
public record RecordLocationRequest(decimal Latitude, decimal Longitude, DateTime RecordedAt);
