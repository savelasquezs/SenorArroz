// SenorArroz.Infrastructure/Repositories/UserRepository.cs
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly IApplicationDbContext _context;

    public UserRepository(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<User?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .Include(u => u.Branch) // Incluye datos de la sucursal
            .FirstOrDefaultAsync(u => u.Id == id , cancellationToken);
    }

    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .Include(u => u.Branch)
            .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower() , cancellationToken);
    }

    public async Task<IEnumerable<User>> GetAllAsync(int? branchId = null, CancellationToken cancellationToken = default)
    {
        // 👇 Aquí forzamos el tipo a IQueryable<User> antes de aplicar Include
        IQueryable<User> query = _context.Users.AsQueryable();

        // 👇 Ahora sí aplicamos el Include sobre IQueryable<User>
        query = query.Include(u => u.Branch);

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
        _context.Users.Update(user);
        await _context.SaveChangesAsync(cancellationToken);

        // Asegurar que Branch esté cargado
        await _context.Users.Entry(user).Reference(u => u.Branch).LoadAsync(cancellationToken);

        return user;
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
        var query = _context.Users.Where(u => u.Email.ToLower() == email.ToLower() );

        // Excluir usuario actual en caso de actualización
        if (excludeUserId.HasValue)
        {
            query = query.Where(u => u.Id != excludeUserId.Value);
        }

        return await query.AnyAsync(cancellationToken);
    }
}