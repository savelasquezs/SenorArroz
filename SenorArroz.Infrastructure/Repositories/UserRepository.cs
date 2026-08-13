// SenorArroz.Infrastructure/Repositories/UserRepository.cs
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;
using System.Data;

namespace SenorArroz.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantExecutionContext? _tenantExecutionContext;

    public UserRepository(IApplicationDbContext context, ITenantExecutionContext? tenantExecutionContext = null)
    {
        _context = context;
        _tenantExecutionContext = tenantExecutionContext;
    }

    public async Task<User?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .AsNoTracking()
            .Include(u => u.Branch)
            .Include(u => u.PayrollExpense)
            .FirstOrDefaultAsync(u => u.Id == id , cancellationToken);
    }

    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .AsNoTracking()
            .Include(u => u.Branch)
            .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower() , cancellationToken);
    }

    public async Task<IEnumerable<User>> GetAllAsync(int? branchId = null, CancellationToken cancellationToken = default)
    {
        IQueryable<User> query = _context.Users.AsNoTracking().AsQueryable();

        query = query.Include(u => u.Branch).Include(u => u.PayrollExpense);

        if (branchId.HasValue)
        {
            query = query.Where(u => u.BranchId == branchId.Value);
        }

        return await query
            .OrderBy(u => u.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<User> AddAsync(User user, CancellationToken cancellationToken = default)
    {
        var entry = await _context.Users.AddAsync(user, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        // Cargar la relación Branch después de guardar
        await entry.Reference(u => u.Branch).LoadAsync(cancellationToken);

        return entry.Entity;
    }

    public async Task<User> UpdateAsync(User user, CancellationToken cancellationToken = default)
    {
        var existing = await _context.Users
            .Include(u => u.Branch)
            .Include(u => u.PayrollExpense)
            .FirstOrDefaultAsync(u => u.Id == user.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Usuario con ID {user.Id} no encontrado");

        if (existing.BranchId != user.BranchId)
            existing.Branch = null!;

        if (existing.PayrollExpenseId != user.PayrollExpenseId)
            existing.PayrollExpense = null;

        existing.BranchId = user.BranchId;
        existing.Role = user.Role;
        existing.Name = user.Name;
        existing.Email = user.Email;
        existing.Phone = user.Phone;
        existing.PasswordHash = user.PasswordHash;
        existing.Active = user.Active;
        existing.WebAccessEnabled = user.WebAccessEnabled;
        existing.ProfileImageUrl = user.ProfileImageUrl;
        existing.ActiveSessionId = user.ActiveSessionId;
        existing.PayrollExpenseId = user.PayrollExpenseId;

        await _context.SaveChangesAsync(cancellationToken);

        return await _context.Users
            .AsNoTracking()
            .Include(u => u.Branch)
            .Include(u => u.PayrollExpense)
            .FirstAsync(u => u.Id == user.Id, cancellationToken);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == id , cancellationToken);

        if (user == null)
            return false;

       
        user.Active = false;

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> EmailExistsAsync(string email, int? excludeUserId = null, CancellationToken cancellationToken = default)
    {
        using var scope = _tenantExecutionContext?.BeginSystemScope();
        var query = _context.Users.IgnoreQueryFilters().Where(u => u.Email.ToLower() == email.ToLower() );

        // Excluir usuario actual en caso de actualización
        if (excludeUserId.HasValue)
        {
            query = query.Where(u => u.Id != excludeUserId.Value);
        }

        return await query.AnyAsync(cancellationToken);
    }
    public async Task<bool> ExistsActiveUserWithRoleInBranch(string role, int branchId, CancellationToken cancellationToken)
{
    return await _context.Users.AnyAsync(u =>
         u.Active && string.Equals(u.Role.ToString(),role, StringComparison.OrdinalIgnoreCase) && u.BranchId == branchId,
    cancellationToken);
    }

public async Task<bool> ExistsActiveSuperAdmin(CancellationToken cancellationToken)
{
        return await _context.Users.AnyAsync(u =>
            u.Active && u.Role == UserRole.Superadmin, cancellationToken);
        
}

    public async Task<bool> UpdateUserPasswordAsync(User user, CancellationToken cancellationToken = default)
    {
        try
        {
            var existing = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == user.Id, cancellationToken);

            if (existing == null)
                return false;

            existing.PasswordHash = user.PasswordHash;
            existing.UpdatedAt = user.UpdatedAt;

            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception)
        {
            // opcional: loguear el error
            return false;
        }
    }
    public async Task<bool> RoleExistsAsync(UserRole role, CancellationToken cancellationToken)
    {
        return await _context.Users
            .AnyAsync(u => u.Role == role, cancellationToken);
    }

    public async Task<bool> AdminExistsInBranchAsync(int branchId, CancellationToken cancellationToken)
    {
        return await _context.Users
            .AnyAsync(u => u.Role == UserRole.Admin && u.BranchId == branchId, cancellationToken);
    }

    public async Task<bool> KitchenExistsInBranchAsync(int branchId, CancellationToken cancellationToken)
    {
        return await _context.Users
            .AnyAsync(u => u.Role == UserRole.Kitchen && u.BranchId == branchId, cancellationToken);
    }

}
