namespace SenorArroz.API.Controllers;

public record RegisterDeviceTokenRequest(string Token, string? Platform);
public record RemoveDeviceTokenRequest(string Token);
