using Microsoft.EntityFrameworkCore;
using Moq;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Dashboard.Queries;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Repositories;

namespace SenorArroz.Tests;

public class DashboardSalesEvolutionOptimizationTests
{
    [Fact]
    public async Task Evolution_UsesSameEffectiveDateAndSalesAcrossAllGranularities()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(nameof(Evolution_UsesSameEffectiveDateAndSalesAcrossAllGranularities)).Options;
        await using var db = new ApplicationDbContext(options, currentTenant: TestTenantContext.Default,
            tenantExecutionContext: TestTenantContext.Default);
        var branch = new Branch { Name = "B", Address = "A", Phone1 = "1", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var user = new User { Name = "Cajero", Email = "qa@local.test" };
        db.Branches.Add(branch);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        DateTime Co(int day, int hour) => ColombiaTimeHelper.ConvertColombiaToUtc(new DateTime(2026, 10, day, hour, 0, 0));
        Order Make(int total, DateTime created, DateTime? prepare = null, OrderStatus status = OrderStatus.Taken) => new()
        {
            BranchId = branch.Id, TakenById = user.Id, Type = OrderType.Onsite,
            Status = status, CreatedAt = created, UpdatedAt = created,
            PrepareAt = prepare, Total = total, Subtotal = total,
        };
        db.Orders.AddRange(
            Make(100, Co(23, 10)),
            Make(200, Co(22, 16), Co(23, 11)),
            Make(900, Co(23, 11), status: OrderStatus.Cancelled));
        await db.SaveChangesAsync();

        var branches = new Mock<IBranchRepository>();
        branches.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { branch });
        var actor = new Mock<ICurrentUser>();
        actor.SetupGet(a => a.Role).Returns("superadmin");
        var handler = new GetDashboardSalesEvolutionHandler(
            new OrderRepository(db, new SystemUtcClock()), branches.Object, actor.Object, db);
        var (from, to) = ColombiaTimeHelper.GetColombiaCalendarDateRangeUtc(
            new DateTime(2026, 10, 23), new DateTime(2026, 10, 23));

        var result = await handler.Handle(new GetDashboardSalesEvolutionQuery
        { FromUtc = from, ToUtc = to, BranchId = branch.Id }, CancellationToken.None);
        Assert.Equal(300, result.SalesByDay.Datasets.SelectMany(s => s.Data).Sum());
        Assert.Equal(300, result.SalesByMonth.Datasets.SelectMany(s => s.Data).Sum());
        Assert.Equal(300, result.SalesByYear.Datasets.SelectMany(s => s.Data).Sum());
        Assert.Equal(300, result.SalesByHour.Datasets.SelectMany(s => s.Data).Sum());
        Assert.Equal(2, result.OrdersByDay.Counts.Sum());
        Assert.Equal(2, result.OrdersByHour.Counts.Sum());

        var otherDay = await handler.Handle(new GetDashboardSalesEvolutionQuery
        { FromUtc = from, ToUtc = to, BranchId = branch.Id, DayOfWeek = 4 }, CancellationToken.None);
        Assert.Equal(0, otherDay.OrdersByDay.Counts.Sum());
    }

    [Fact]
    public async Task Evolution_DeniesAdminWithoutAssignedBranch()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(nameof(Evolution_DeniesAdminWithoutAssignedBranch)).Options;
        await using var db = new ApplicationDbContext(options, currentTenant: TestTenantContext.Default,
            tenantExecutionContext: TestTenantContext.Default);
        var actor = new Mock<ICurrentUser>();
        actor.SetupGet(a => a.Role).Returns("admin");
        actor.SetupGet(a => a.BranchId).Returns(0);
        var handler = new GetDashboardSalesEvolutionHandler(
            new OrderRepository(db, new SystemUtcClock()), Mock.Of<IBranchRepository>(), actor.Object, db);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.Handle(
            new GetDashboardSalesEvolutionQuery { FromUtc = DateTime.UtcNow.AddDays(-1), ToUtc = DateTime.UtcNow },
            CancellationToken.None));
    }
}
