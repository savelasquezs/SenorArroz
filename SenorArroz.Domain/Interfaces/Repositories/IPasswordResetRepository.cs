using SenorArroz.Domain.Entities;

namespace SenorArroz.Domain.Interfaces.Repositories;

public interface IPasswordResetRepository
{
    Task<PasswordResetToken?> GetByTokenAsync(string token, CancellationToken cancellationToken = default);
    Task<PasswordResetToken?> GetValidTokenByUserIdAsync(int userId, CancellationToken cancellationToken = default);
    Task<IEnumerable<PasswordResetToken>> GetByUserIdAsync(int userId, CancellationToken cancellationToken = default);
    Task<PasswordResetToken> CreateAsync(PasswordResetToken passwordResetToken, CancellationToken cancellationToken = default);
    Task UpdateAsync(PasswordResetToken passwordResetToken, CancellationToken cancellationToken = default);
    Task InvalidateAllUserTokensAsync(int userId, CancellationToken cancellationToken = default);
    Task DeleteExpiredTokensAsync(CancellationToken cancellationToken = default);
}
