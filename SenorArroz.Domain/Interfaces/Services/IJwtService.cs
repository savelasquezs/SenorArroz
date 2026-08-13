using SenorArroz.Domain.Entities;

namespace SenorArroz.Domain.Interfaces.Services;

public interface IJwtService
{
    string GenerateAccessToken(
        User user,
        Guid? sessionId = null,
        string? deviceInstallationId = null);
    string GenerateRefreshToken();
    int? GetUserIdFromExpiredToken(string token);
    int? GetTenantIdFromExpiredToken(string token);
    Guid? GetSessionIdFromExpiredToken(string token);
    string? GetDeviceInstallationIdFromExpiredToken(string token);
    bool IsTokenExpired(string token);
}
