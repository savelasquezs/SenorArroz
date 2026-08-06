using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Repositories;

namespace SenorArroz.Tests;

public sealed class UserRepositoryTests
{
    [Fact]
    public async Task UpdateAsync_PersistsBranchId_WhenDetachedUserHasOldBranchNavigation()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ApplicationDbContext(options);
        var now = new DateTime(2026, 8, 6, 12, 0, 0, DateTimeKind.Utc);
        var santander = new Branch
        {
            Name = "Santander",
            Address = "Calle 1",
            Phone1 = "3000000000",
            CreatedAt = now,
            UpdatedAt = now
        };
        var manrique = new Branch
        {
            Name = "Manrique",
            Address = "Calle 2",
            Phone1 = "3000000001",
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Branches.AddRange(santander, manrique);
        await db.SaveChangesAsync();

        var user = new User
        {
            BranchId = santander.Id,
            Role = UserRole.Deliveryman,
            Name = "Pipe",
            Email = "pipe@example.com",
            Phone = "3146777140",
            PasswordHash = "hash",
            Active = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new UserRepository(db);
        var detached = await repository.GetByIdAsync(user.Id);
        Assert.NotNull(detached);
        Assert.Equal(santander.Id, detached.Branch.Id);

        detached.BranchId = manrique.Id;
        var updated = await repository.UpdateAsync(detached);

        db.ChangeTracker.Clear();
        var persisted = await db.Users.AsNoTracking().SingleAsync(x => x.Id == user.Id);
        Assert.Equal(manrique.Id, persisted.BranchId);
        Assert.Equal(manrique.Id, updated.BranchId);
        Assert.Equal("Manrique", updated.Branch.Name);
    }
}
