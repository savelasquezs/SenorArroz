using Microsoft.EntityFrameworkCore;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.Queries;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests;

public sealed class OrderDeliverymanOptionsTests
{
    private static ApplicationDbContext Database()
    {
        var tenant = new TestTenantContext(1);
        return new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, currentTenant: tenant, tenantExecutionContext: tenant);
    }

    private static GetOrderDeliverymanOptionsHandler Handler(ApplicationDbContext db)
    {
        var user = new Mock<ICurrentUser>();
        user.SetupGet(x => x.Role).Returns("Cashier");
        user.SetupGet(x => x.TenantId).Returns(1);
        user.SetupGet(x => x.BranchId).Returns(1);
        var branches = new Mock<IBranchContext>();
        branches.Setup(x => x.RequireBranch(It.IsAny<int?>())).Returns(1);
        return new(db, user.Object, branches.Object);
    }

    [Fact]
    public async Task Includes_drivers_after_200_orders_without_returning_order_graphs()
    {
        await using var db = Database();
        var first = new User { Id = 10, TenantId = 1, Name = "First" };
        var last = new User { Id = 11, TenantId = 1, Name = "Last" };
        db.Users.AddRange(first, last);
        for (var index = 0; index < 205; index++) db.Orders.Add(new Order { TenantId = 1, BranchId = 1,
            Type = OrderType.Delivery, DeliveryManId = index == 204 ? last.Id : first.Id });
        db.Orders.Add(new Order { TenantId = 1, BranchId = 2, Type = OrderType.Delivery, DeliveryManId = 12 });
        await db.SaveChangesAsync();
        var result = await Handler(db).Handle(new(null, null, null, false), default);
        Assert.Equal(new[] { 10, 11 }, result.Select(item => item.Id));
    }

    [Fact]
    public async Task Remote_options_only_include_orders_taken_from_the_current_workspace()
    {
        await using var db = Database();
        db.Branches.Add(new Branch { Id = 2, TenantId = 1, Name = "Destination" });
        db.Orders.AddRange(new Order { TenantId = 1, BranchId = 2, OriginBranchId = 1, Type = OrderType.Delivery, DeliveryManId = 10 },
            new Order { TenantId = 1, BranchId = 2, OriginBranchId = 3, Type = OrderType.Delivery, DeliveryManId = 11 });
        await db.SaveChangesAsync();
        var result = await Handler(db).Handle(new(null, null, 2, true), default);
        Assert.Equal(10, Assert.Single(result).Id);
        await Assert.ThrowsAsync<BranchAccessDeniedException>(() => Handler(db).Handle(new(null, null, 99, true), default));
    }

    [Fact]
    public async Task Propagates_cancellation()
    {
        await using var db = Database();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Handler(db).Handle(new(null, null, null, false), cancellation.Token));
    }
}
