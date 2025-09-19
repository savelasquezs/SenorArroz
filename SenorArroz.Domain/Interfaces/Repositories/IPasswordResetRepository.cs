using SenorArroz.Domain.Entities;

namespace SenorArroz.Domain.Interfaces.Repositories;

public interface IPasswordResetRepository
{
    Task<PasswordResetToken?> GetByTokenAsync(string token);
    Task<PasswordResetToken?> GetValidTokenByUserIdAsync(int userId);
    Task<IEnumerable<PasswordResetToken>> GetByUserIdAsync(int userId);
    Task<PasswordResetToken> CreateAsync(PasswordResetToken passwordResetToken);
    Task UpdateAsync(PasswordResetToken passwordResetToken);
    Task InvalidateAllUserTokensAsync(int userId);
    Task DeleteExpiredTokensAsync();
}