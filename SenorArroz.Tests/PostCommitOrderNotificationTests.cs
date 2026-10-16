using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SenorArroz.API.Services;
using SenorArroz.Application.Common.Behaviors;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.Commands;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests;

public sealed class PostCommitOrderNotificationTests
{
    private sealed class Actor : ICurrentUser
    {
        public int Id => 3;
        public int TenantId => 1;
        public int BranchId => 1;
        public string Role => "Admin";
        public bool IsAuthenticated => true;
    }

    [Fact]
    public async Task Notification_is_deferred_until_mutation_has_saved_all_changes()
    {
        await using var db = CreateDb();
        db.Orders.Add(ReadyOrder());
        await db.SaveChangesAsync();
        var context = CreateContext();
        var behavior = Behavior(db, context);
        var eventsSent = 0;

        var response = await behavior.Handle(new AssignDeliveryManCommand { Id = 15 }, async _ =>
        {
            await OrderPostCommitNotifications.Publish(context, async () =>
            {
                Assert.Equal(2, (await db.Orders.SingleAsync()).ManagementVersion);
                eventsSent++;
            });
            Assert.Equal(0, eventsSent);
            return new OrderDto { Id = 15 };
        }, default);

        Assert.Equal(1, eventsSent);
        Assert.Equal(2, response.ManagementVersion);
        Assert.Empty(context.DrainDeferredNotifications());
    }

    [Fact]
    public async Task Failed_mutation_never_publishes_queued_events()
    {
        await using var db = CreateDb();
        db.Orders.Add(ReadyOrder());
        await db.SaveChangesAsync();
        var context = CreateContext();
        var calls = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Behavior(db, context).Handle(
            new AssignDeliveryManCommand { Id = 15 },
            async _ =>
            {
                await OrderPostCommitNotifications.Publish(context, () =>
                {
                    calls++;
                    return Task.CompletedTask;
                });
                throw new InvalidOperationException("Aborted");
            }, default));

        Assert.Equal(0, calls);
        Assert.Empty(context.DrainDeferredNotifications());
    }

    private static OrderMutationBehavior<AssignDeliveryManCommand, OrderDto> Behavior(
        ApplicationDbContext db, HttpOrderMutationContext context) =>
        new(db, new Actor(), new TestBranchContext(1), Mock.Of<IOrderNotificationService>(),
            NullLogger<OrderMutationBehavior<AssignDeliveryManCommand, OrderDto>>.Instance, context);

    private static HttpOrderMutationContext CreateContext() =>
        new(new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

    private static Order ReadyOrder() => new()
    {
        Id = 15, TenantId = 1, BranchId = 1, TakenById = 3,
        Type = OrderType.Delivery, Status = OrderStatus.Ready,
        ManagementVersion = 1, StatusTimes = "{}"
    };

    private static ApplicationDbContext CreateDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        currentTenant: TestTenantContext.Default,
        tenantExecutionContext: TestTenantContext.Default);
}
