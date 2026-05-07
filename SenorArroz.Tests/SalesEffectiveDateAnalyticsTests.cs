using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Services;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Repositories;

namespace SenorArroz.Tests;

public class SalesEffectiveDateAnalyticsTests
{
    private static ApplicationDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task<(Branch Branch, User User)> SeedBaseAsync(ApplicationDbContext ctx)
    {
        var branch = new Branch { Name = "B1", Address = "A1", Phone1 = "1", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var user = new User { Name = "U1", Email = "u1@test.local" };
        ctx.Branches.Add(branch);
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return (branch, user);
    }

    [Fact]
    public async Task GetTotalSales_UsesPrepareAtOverCreatedAt_ForRange()
    {
        using var ctx = CreateContext(nameof(GetTotalSales_UsesPrepareAtOverCreatedAt_ForRange));
        var (branch, user) = await SeedBaseAsync(ctx);
        var repo = new OrderRepository(ctx, new SystemUtcClock());

        var createdToday = new DateTime(2026, 5, 7, 15, 0, 0, DateTimeKind.Utc);
        var prepareTomorrow = new DateTime(2026, 5, 8, 13, 0, 0, DateTimeKind.Utc);

        ctx.Orders.Add(new Order
        {
            BranchId = branch.Id,
            TakenById = user.Id,
            Status = OrderStatus.Taken,
            Type = OrderType.Onsite,
            Total = 300000,
            Subtotal = 300000,
            CreatedAt = createdToday,
            PrepareAt = prepareTomorrow,
            ReservedFor = createdToday,
            UpdatedAt = createdToday
        });
        await ctx.SaveChangesAsync();

        var (todayFrom, todayTo) = ColombiaTimeHelper.GetColombiaCalendarDateRangeUtc(new DateTime(2026, 5, 7), new DateTime(2026, 5, 7));
        var (tomorrowFrom, tomorrowTo) = ColombiaTimeHelper.GetColombiaCalendarDateRangeUtc(new DateTime(2026, 5, 8), new DateTime(2026, 5, 8));

        var todaySales = await repo.GetTotalSalesAsync(branch.Id, todayFrom, todayTo);
        var tomorrowSales = await repo.GetTotalSalesAsync(branch.Id, tomorrowFrom, tomorrowTo);

        Assert.Equal(0, todaySales);
        Assert.Equal(300000, tomorrowSales);
    }

    [Fact]
    public async Task GetTotalSales_UsesCreatedAt_WhenPrepareAtIsNull()
    {
        using var ctx = CreateContext(nameof(GetTotalSales_UsesCreatedAt_WhenPrepareAtIsNull));
        var (branch, user) = await SeedBaseAsync(ctx);
        var repo = new OrderRepository(ctx, new SystemUtcClock());

        var created = new DateTime(2026, 5, 7, 16, 0, 0, DateTimeKind.Utc);
        ctx.Orders.Add(new Order
        {
            BranchId = branch.Id,
            TakenById = user.Id,
            Status = OrderStatus.InPreparation,
            Type = OrderType.Onsite,
            Total = 100000,
            Subtotal = 100000,
            CreatedAt = created,
            PrepareAt = null,
            ReservedFor = created.AddDays(3),
            UpdatedAt = created
        });
        await ctx.SaveChangesAsync();

        var (from, to) = ColombiaTimeHelper.GetColombiaCalendarDateRangeUtc(new DateTime(2026, 5, 7), new DateTime(2026, 5, 7));
        var sales = await repo.GetTotalSalesAsync(branch.Id, from, to);
        Assert.Equal(100000, sales);
    }

    [Fact]
    public async Task GetTotalSales_PartialRange_UsesSalesEffectiveDate()
    {
        using var ctx = CreateContext(nameof(GetTotalSales_PartialRange_UsesSalesEffectiveDate));
        var (branch, user) = await SeedBaseAsync(ctx);
        var repo = new OrderRepository(ctx, new SystemUtcClock());

        ctx.Orders.AddRange(
            new Order
            {
                BranchId = branch.Id, TakenById = user.Id, Status = OrderStatus.Taken, Type = OrderType.Onsite,
                Total = 10000, Subtotal = 10000,
                CreatedAt = new DateTime(2026, 5, 7, 12, 0, 0, DateTimeKind.Utc),
                PrepareAt = new DateTime(2026, 5, 6, 12, 0, 0, DateTimeKind.Utc),
                UpdatedAt = DateTime.UtcNow
            },
            new Order
            {
                BranchId = branch.Id, TakenById = user.Id, Status = OrderStatus.Taken, Type = OrderType.Onsite,
                Total = 20000, Subtotal = 20000,
                CreatedAt = new DateTime(2026, 5, 6, 12, 0, 0, DateTimeKind.Utc),
                PrepareAt = new DateTime(2026, 5, 7, 12, 0, 0, DateTimeKind.Utc),
                UpdatedAt = DateTime.UtcNow
            });
        await ctx.SaveChangesAsync();

        var from = new DateTime(2026, 5, 7, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 5, 7, 23, 59, 59, DateTimeKind.Utc);
        var onlyFrom = await repo.GetTotalSalesAsync(branch.Id, from, null);
        var onlyTo = await repo.GetTotalSalesAsync(branch.Id, null, to);

        Assert.Equal(20000, onlyFrom);
        Assert.Equal(30000, onlyTo);
    }
}
