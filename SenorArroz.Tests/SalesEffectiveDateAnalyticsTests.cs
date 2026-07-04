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

    [Fact]
    public async Task GetDashboardSalesHourlyAnalytics_GroupsByColombiaDateHourBeforeMedian()
    {
        using var ctx = CreateContext(nameof(GetDashboardSalesHourlyAnalytics_GroupsByColombiaDateHourBeforeMedian));
        var (branch, user) = await SeedBaseAsync(ctx);
        var repo = new OrderRepository(ctx, new SystemUtcClock());

        DateTime Co(int year, int month, int day, int hour, int minute = 0) =>
            ColombiaTimeHelper.ConvertColombiaToUtc(new DateTime(year, month, day, hour, minute, 0));

        Order Order(DateTime createdAt, int total, DateTime? prepareAt = null, OrderStatus status = OrderStatus.Taken) => new()
        {
            BranchId = branch.Id,
            TakenById = user.Id,
            Status = status,
            Type = OrderType.Onsite,
            Total = total,
            Subtotal = total,
            CreatedAt = createdAt,
            PrepareAt = prepareAt,
            UpdatedAt = createdAt
        };

        ctx.Orders.AddRange(
            Order(Co(2026, 7, 2, 11, 10), 100),
            Order(Co(2026, 7, 2, 11, 30), 200),
            Order(Co(2026, 7, 9, 11, 15), 500),
            Order(Co(2026, 7, 2, 12, 0), 1000),
            Order(Co(2026, 7, 3, 11, 0), 999),
            Order(Co(2026, 7, 2, 11, 45), 700, status: OrderStatus.Cancelled),
            Order(Co(2026, 7, 1, 18, 0), 300, Co(2026, 7, 9, 11, 45)));
        await ctx.SaveChangesAsync();

        var (from, to) = ColombiaTimeHelper.GetColombiaCalendarDateRangeUtc(
            new DateTime(2026, 7, 1),
            new DateTime(2026, 7, 31));

        var rows = await repo.GetDashboardSalesHourlyAnalyticsAsync(branch.Id, from, to, 4);

        var eleven = Assert.Single(rows, r => r.Hour == 11);
        Assert.Equal(4, eleven.OrderCount);
        Assert.Equal(1100, eleven.TotalSalesCop);
        Assert.Equal(550m, eleven.AverageDailySalesCop);
        Assert.Equal(550m, eleven.MedianDailySalesCop);
        Assert.Equal(275m, eleven.AverageTicketCop);

        var noon = Assert.Single(rows, r => r.Hour == 12);
        Assert.Equal(1, noon.OrderCount);
        Assert.Equal(1000, noon.TotalSalesCop);
        Assert.Equal(1000m, noon.MedianDailySalesCop);
    }
}
