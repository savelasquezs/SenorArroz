using SenorArroz.Domain.Entities;

namespace SenorArroz.Domain.Interfaces.Repositories;

public interface IAuthRepository
{
    Task<User?> GetUserByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<User?> GetUserByIdWithBranchAsync(int userId, CancellationToken cancellationToken = default);
    Task<bool> IsSessionCurrentAsync(int userId, Guid? sessionId, CancellationToken cancellationToken = default);
    Task<bool> CanDeliverymanAccessWebAsync(int userId, CancellationToken cancellationToken = default);
    Task EndSessionIfCurrentAsync(int userId, Guid sessionId, CancellationToken cancellationToken = default);
    Task<bool> ValidatePasswordAsync(User user, string password, CancellationToken cancellationToken = default);
    Task<bool> ChangePasswordAsync(int userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default);
    Task<bool> UpdateUserPasswordAsync(User user, string newPasswordHash, CancellationToken cancellationToken = default);
}
