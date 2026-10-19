using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Domain.Interfaces.Repositories
{
    public interface IUserRepository
    {
        Task<User?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

        Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

        Task<IEnumerable<User>> GetAllAsync(int? branchId = null, CancellationToken cancellationToken = default);

        Task<User> AddAsync(User user, CancellationToken cancellationToken = default);

        Task<User> UpdateAsync(User user, CancellationToken cancellationToken = default);

        Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);

        Task<bool> UpdateUserPasswordAsync(User user, CancellationToken cancellationToken = default);
        Task<bool> EmailExistsAsync(string email, int? excludeUserId = null, CancellationToken cancellationToken = default);
        Task<bool> RoleExistsAsync(UserRole role, CancellationToken cancellationToken);
        Task<bool> AdminExistsInBranchAsync(int branchId, CancellationToken cancellationToken);

    }
}
