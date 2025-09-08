using SenorArroz.Domain.Entities;

namespace SenorArroz.Domain.Interfaces.Repositories;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByTokenAsync(string token);
    Task<RefreshToken?> GetActiveByUserIdAsync(int userId);
    Task<IEnumerable<RefreshToken>> GetAllByUserIdAsync(int userId);
    Task AddAsync(RefreshToken refreshToken);
    Task UpdateAsync(RefreshToken refreshToken);
    Task RevokeAllByUserIdAsync(int userId, string ipAddress);
    Task DeleteExpiredTokensAsync();
}