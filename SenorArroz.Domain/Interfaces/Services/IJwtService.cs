using SenorArroz.Domain.Entities;

namespace SenorArroz.Domain.Interfaces.Services;

public interface IJwtService
{
    string GenerateAccessToken(User user);
    string GenerateRefreshToken();
    int? GetUserIdFromExpiredToken(string token);
    bool IsTokenExpired(string token);
}