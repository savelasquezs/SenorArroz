using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Repositories;

namespace SenorArroz.Tests;

public class PasswordResetRepositoryTrackingTests
{
    [Fact]
    public async Task UpdateAsync_DoesNotAttachDetachedUserGraph_WhenSameUserIsTracked()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(nameof(UpdateAsync_DoesNotAttachDetachedUserGraph_WhenSameUserIsTracked))
            .Options;
        await using var context = new ApplicationDbContext(options);
        var now = new DateTime(2026, 7, 19, 21, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);

        var branch = new Branch
        {
            Name = "Sucursal Test",
            Address = "Calle 1",
            Phone1 = "0000000",
            CreatedAt = now,
            UpdatedAt = now
        };
        context.Branches.Add(branch);
        await context.SaveChangesAsync();

        var user = new User
        {
            Name = "Usuario Test",
            Email = "usuario@test.com",
            PasswordHash = "old-hash",
            Role = UserRole.Deliveryman,
            BranchId = branch.Id,
            Active = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var token = PasswordResetToken.Create(user.Id, user.Email, 60, now);
        context.PasswordResetTokens.Add(token);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var repository = new PasswordResetRepository(context, clock);
        var detachedToken = await repository.GetByTokenAsync(token.Token);
        Assert.NotNull(detachedToken);

        // Reproduce the production flow: password update tracks another User
        // instance before the detached reset token is marked as used.
        var trackedUser = await context.Users.SingleAsync(x => x.Id == user.Id);
        trackedUser.PasswordHash = "new-hash";
        await context.SaveChangesAsync();

        detachedToken.MarkAsUsed("127.0.0.1", now.AddMinutes(1));
        await repository.UpdateAsync(detachedToken);

        context.ChangeTracker.Clear();
        var persistedToken = await context.PasswordResetTokens.SingleAsync(x => x.Id == token.Id);
        Assert.True(persistedToken.IsUsed);
        Assert.Equal(now.AddMinutes(1), persistedToken.UsedAt);
        Assert.Equal("127.0.0.1", persistedToken.UsedByIp);
    }
}
