using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Repositories;

namespace SenorArroz.Tests;

public class OrderRepositoryStatusTransitionTests
{
    private static ApplicationDbContext CreateCtx(string dbName)
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new ApplicationDbContext(opts);
    }

    [Fact]
    public async Task ChangeStatusAsync_uncancels_to_ready_and_clears_cancelled_reason()
    {
        using var ctx = CreateCtx(nameof(ChangeStatusAsync_uncancels_to_ready_and_clears_cancelled_reason));
        var utcNow = new DateTime(2026, 6, 1, 16, 30, 0, DateTimeKind.Utc);
        var cancelledAt = new DateTime(2026, 6, 1, 15, 0, 0, DateTimeKind.Utc);

        var branch = new Branch
        {
            Name = "Test",
            Address = "-",
            Phone1 = "-",
            CreatedAt = utcNow,
        };
        ctx.Branches.Add(branch);
        await ctx.SaveChangesAsync();

        var user = new User
        {
            Name = "Admin",
            Email = $"admin_{Guid.NewGuid()}@test.com",
            PasswordHash = "hash",
            Role = UserRole.Admin,
            BranchId = branch.Id,
            Active = true,
            CreatedAt = utcNow,
        };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var order = new Order
        {
            BranchId = branch.Id,
            TakenById = user.Id,
            Type = OrderType.Onsite,
            Status = OrderStatus.Cancelled,
            StatusTimes = "{}",
            CancelledReason = "Cliente cancelo",
            Subtotal = 10000,
            Total = 10000,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
        };
        order.AddStatusTime(OrderStatus.Cancelled, cancelledAt);
        ctx.Orders.Add(order);
        await ctx.SaveChangesAsync();

        var repo = new OrderRepository(ctx, new FakeClock(utcNow));

        var updated = await repo.ChangeStatusAsync(order.Id, OrderStatus.Ready);

        Assert.Equal(OrderStatus.Ready, updated.Status);

        var persisted = await ctx.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        Assert.Null(persisted.CancelledReason);

        var statusTimes = persisted.GetStatusTimes();
        Assert.Equal(cancelledAt, statusTimes["cancelled"]);
        Assert.Equal(utcNow, statusTimes["ready"]);
    }

    [Fact]
    public async Task ChangeStatusAsync_allows_ready_to_return_to_in_preparation()
    {
        using var ctx = CreateCtx(nameof(ChangeStatusAsync_allows_ready_to_return_to_in_preparation));
        var utcNow = new DateTime(2026, 7, 24, 16, 30, 0, DateTimeKind.Utc);

        var branch = new Branch
        {
            Name = "Test",
            Address = "-",
            Phone1 = "-",
            CreatedAt = utcNow,
        };
        ctx.Branches.Add(branch);
        await ctx.SaveChangesAsync();

        var user = new User
        {
            Name = "Admin",
            Email = $"admin_{Guid.NewGuid()}@test.com",
            PasswordHash = "hash",
            Role = UserRole.Admin,
            BranchId = branch.Id,
            Active = true,
            CreatedAt = utcNow,
        };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var order = new Order
        {
            BranchId = branch.Id,
            TakenById = user.Id,
            Type = OrderType.Onsite,
            Status = OrderStatus.Ready,
            StatusTimes = "{}",
            Subtotal = 10000,
            Total = 10000,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
        };
        ctx.Orders.Add(order);
        await ctx.SaveChangesAsync();

        var repo = new OrderRepository(ctx, new FakeClock(utcNow));

        var updated = await repo.ChangeStatusAsync(order.Id, OrderStatus.InPreparation);

        Assert.Equal(OrderStatus.InPreparation, updated.Status);
        Assert.Equal(
            utcNow,
            (await ctx.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id))
                .GetStatusTimes()["inpreparation"]);
    }

    [Theory]
    [InlineData(OrderStatus.Taken)]
    [InlineData(OrderStatus.InPreparation)]
    [InlineData(OrderStatus.Ready)]
    [InlineData(OrderStatus.OnTheWay)]
    [InlineData(OrderStatus.Delivered)]
    public async Task CanChangeStatusAsync_allows_targets_from_cancelled_after_application_authorization(OrderStatus target)
    {
        using var ctx = CreateCtx($"{nameof(CanChangeStatusAsync_allows_targets_from_cancelled_after_application_authorization)}_{target}");
        var utcNow = new DateTime(2026, 6, 1, 16, 30, 0, DateTimeKind.Utc);

        var branch = new Branch
        {
            Name = "Test",
            Address = "-",
            Phone1 = "-",
            CreatedAt = utcNow,
        };
        ctx.Branches.Add(branch);
        await ctx.SaveChangesAsync();

        var user = new User
        {
            Name = "Admin",
            Email = $"admin_{Guid.NewGuid()}@test.com",
            PasswordHash = "hash",
            Role = UserRole.Admin,
            BranchId = branch.Id,
            Active = true,
            CreatedAt = utcNow,
        };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var order = new Order
        {
            BranchId = branch.Id,
            TakenById = user.Id,
            Type = OrderType.Onsite,
            Status = OrderStatus.Cancelled,
            StatusTimes = "{}",
            Subtotal = 10000,
            Total = 10000,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
        };
        ctx.Orders.Add(order);
        await ctx.SaveChangesAsync();

        var repo = new OrderRepository(ctx, new FakeClock(utcNow));

        Assert.True(await repo.CanChangeStatusAsync(order.Id, target));
    }

    [Fact]
    public async Task ChangeStatusAsync_allows_delivered_to_ready_after_application_authorization()
    {
        using var ctx = CreateCtx(nameof(ChangeStatusAsync_allows_delivered_to_ready_after_application_authorization));
        var utcNow = new DateTime(2026, 8, 2, 17, 30, 0, DateTimeKind.Utc);

        var branch = new Branch
        {
            Name = "Test",
            Address = "-",
            Phone1 = "-",
            CreatedAt = utcNow,
        };
        ctx.Branches.Add(branch);
        await ctx.SaveChangesAsync();

        var user = new User
        {
            Name = "Superadmin",
            Email = $"superadmin_{Guid.NewGuid()}@test.com",
            PasswordHash = "hash",
            Role = UserRole.Superadmin,
            BranchId = branch.Id,
            Active = true,
            CreatedAt = utcNow,
        };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var order = new Order
        {
            BranchId = branch.Id,
            TakenById = user.Id,
            Type = OrderType.Onsite,
            Status = OrderStatus.Delivered,
            StatusTimes = "{}",
            Subtotal = 10000,
            Total = 10000,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
        };
        ctx.Orders.Add(order);
        await ctx.SaveChangesAsync();

        var repo = new OrderRepository(ctx, new FakeClock(utcNow));

        var updated = await repo.ChangeStatusAsync(order.Id, OrderStatus.Ready);

        Assert.Equal(OrderStatus.Ready, updated.Status);
        Assert.Equal(
            utcNow,
            (await ctx.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id))
                .GetStatusTimes()["ready"]);
    }
}
