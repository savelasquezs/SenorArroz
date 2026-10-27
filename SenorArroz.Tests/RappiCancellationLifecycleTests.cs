using System.Reflection;
using System.Data.Common;
using System.Text.Json;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SenorArroz.Application.Common.Behaviors;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.AppPayments.Commands;
using SenorArroz.Application.Features.Inventory.Services;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Integrations;

namespace SenorArroz.Tests;

/// <summary>Real disposable PostgreSQL/inventory; external transports are always doubles.</summary>
public sealed class RappiCancellationLifecycleTests : RappiReadyRecoveryDatabase
{
    private static readonly DateTime Now = new(2026, 10, 21, 17, 0, 0, DateTimeKind.Utc);
    private const string Cancel = """{"order_id":"ready-test-order","reason":"synthetic cancellation"}""";

    [PostgreSqlIntegrationFact]
    public async Task Failure_after_real_inventory_release_rolls_back_order_payment_and_stock()
    {
        await using var db = Db();
        await SeedAsync(db);
        var actualInventory = Inventory(db);
        var faulting = new Mock<IInventoryService>(MockBehavior.Strict);
        faulting.Setup(x => x.ReleaseOrderAsync(It.IsAny<Order>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (Order order, string key, CancellationToken ct) =>
            {
                await actualInventory.ReleaseOrderAsync(order, key, ct);
                throw new InjectedFailure();
            });
        var processor = Processor(db, faulting.Object);

        await Assert.ThrowsAsync<InjectedFailure>(() => Invoke(processor, "ApplyCancellationAsync", Cancel));

        await using var check = Db();
        Assert.Equal(OrderStatus.Taken, (await check.Orders.SingleAsync()).Status);
        Assert.False((await check.AppPayments.SingleAsync()).IsReversed);
        Assert.Equal(2, (await check.OrderInventoryAllocations.SingleAsync()).ReservedQuantity);
        Assert.Equal(2, (await check.InventoryBalances.SingleAsync()).QuantityReserved);
        Assert.Empty(await check.InventoryMovements.ToListAsync());
    }

    [PostgreSqlIntegrationFact]
    public async Task Cancellation_before_new_order_keeps_a_durable_terminal_marker()
    {
        await using var db = Db();
        await SeedAsync(db);
        var differentOrder = """{"order_id":"cancel-before-new","reason":"synthetic cancellation"}""";
        var provider = new Mock<IRappiDeliveryProvider>(MockBehavior.Strict);
        var processor = Processor(db, provider: provider.Object);

        await Invoke(processor, "ApplyCancellationAsync", differentOrder);
        await Invoke(processor, "ApplyCancellationAsync", differentOrder);
        db.ChangeTracker.Clear();
        var result = await processor.IngestNewOrderAsync(1,
            """{"order_id":"cancel-before-new","store":{"internal_id":"local-test-store"}}""", default);

        Assert.False(result.Success);
        Assert.True(result.Held);
        var marker = await db.ExternalDeliveryOrders.SingleAsync(x => x.ExternalOrderId == "cancel-before-new");
        Assert.Equal(ExternalOrderStatus.Cancelled, marker.Status);
        Assert.Null(marker.InternalOrderId);
        Assert.Equal(1, await db.Orders.CountAsync());
        Assert.Equal(1, await db.AppPayments.CountAsync());
        provider.VerifyNoOtherCalls();
    }

    [PostgreSqlIntegrationFact]
    public async Task Close_before_handoff_consumes_real_inventory_once_and_keeps_historical_time()
    {
        await using var db = Db();
        await SeedAsync(db);
        var repository = new Mock<IOrderRepository>(MockBehavior.Strict);
        repository.Setup(x => x.GetByIdWithFullDetailsAsync(1, It.IsAny<CancellationToken>()))
            .Returns(async () => await db.Orders.SingleAsync());
        var mapper = new Mock<IMapper>(MockBehavior.Strict);
        mapper.Setup(x => x.Map<OrderDto>(It.IsAny<object>()))
            .Returns((object input) => new OrderDto { Id = 1, BranchId = 1, Status = ((Order)input).Status });
        var notifications = new Mock<IOrderNotificationService>(MockBehavior.Strict);
        notifications.Setup(x => x.NotifyOrderModifiedToKitchen(It.Is<OrderDto>(o => o.Status == OrderStatus.Delivered), "status", null))
            .Returns(Task.CompletedTask);
        notifications.Setup(x => x.NotifyOrderModifiedToDelivery(It.Is<OrderDto>(o => o.Status == OrderStatus.Delivered), "status", null))
            .Returns(Task.CompletedTask);
        notifications.Setup(x => x.NotifyOrderManagementChanged(1, 1, 1, It.IsAny<long>()))
            .Returns(Task.CompletedTask);
        var processor = Processor(db, Inventory(db), notifications.Object, repository: repository.Object, mapper: mapper.Object);
        await Invoke(processor, "ApplyOrderEventAsync", """{"order_id":"ready-test-order","event":"close_order"}""");
        await db.SaveChangesAsync();
        var closedAt = JsonDocument.Parse((await db.Orders.SingleAsync()).StatusTimes)
            .RootElement.GetProperty("delivered").GetDateTime();
        await Invoke(processor, "ApplyOrderEventAsync", """{"order_id":"ready-test-order","event":"close_order"}""");
        await Invoke(processor, "ApplyOrderEventAsync", """{"order_id":"ready-test-order","event":"hand_to_domiciliary"}""");
        await db.SaveChangesAsync();

        await using var check = Db();
        Assert.Equal(OrderStatus.Delivered, (await check.Orders.SingleAsync()).Status);
        Assert.Equal(closedAt, JsonDocument.Parse((await check.Orders.SingleAsync()).StatusTimes)
            .RootElement.GetProperty("delivered").GetDateTime());
        Assert.Equal(2, (await check.OrderInventoryAllocations.SingleAsync()).ConsumedQuantity);
        Assert.Equal(0, (await check.InventoryBalances.SingleAsync()).QuantityReserved);
        Assert.Equal(8, (await check.InventoryBalances.SingleAsync()).QuantityOnHand);
        Assert.Single(await check.InventoryMovements.ToListAsync());
        Assert.False((await check.AppPayments.SingleAsync()).IsReversed);
        notifications.Verify(x => x.NotifyOrderModifiedToKitchen(It.IsAny<OrderDto>(), "status", null), Times.Exactly(3));
        notifications.Verify(x => x.NotifyOrderModifiedToDelivery(It.IsAny<OrderDto>(), "status", null), Times.Exactly(3));
        notifications.Verify(x => x.NotifyOrderManagementChanged(1, 1, 1, It.IsAny<long>()), Times.Exactly(3));
        notifications.VerifyNoOtherCalls();
    }

    [PostgreSqlIntegrationFact]
    public async Task Failure_after_real_consumption_rolls_back_before_delivered()
    {
        await using var db = Db();
        await SeedAsync(db);
        var actualInventory = Inventory(db);
        var faulting = new Mock<IInventoryService>(MockBehavior.Strict);
        faulting.Setup(x => x.ConsumeOrderAsync(It.IsAny<Order>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (Order order, string key, CancellationToken ct) =>
            {
                await actualInventory.ConsumeOrderAsync(order, key, ct);
                throw new InjectedFailure();
            });

        await Assert.ThrowsAsync<InjectedFailure>(() => Invoke(Processor(db, faulting.Object), "ApplyOrderEventAsync",
            """{"order_id":"ready-test-order","event":"close_order"}"""));

        await using var check = Db();
        Assert.Equal(OrderStatus.Taken, (await check.Orders.SingleAsync()).Status);
        Assert.Equal(0, (await check.OrderInventoryAllocations.SingleAsync()).ConsumedQuantity);
        Assert.Equal(2, (await check.InventoryBalances.SingleAsync()).QuantityReserved);
        Assert.Equal(10, (await check.InventoryBalances.SingleAsync()).QuantityOnHand);
        Assert.Empty(await check.InventoryMovements.ToListAsync());
        Assert.False((await check.AppPayments.SingleAsync()).IsReversed);
    }

    [PostgreSqlIntegrationFact]
    public async Task Inbox_replays_postcommit_effects_after_notification_failure_without_repeating_core()
    {
        await using var db = Db();
        await SeedAsync(db);
        await AddCancelEventAsync(db);
        var sends = 0;
        var notifications = new Mock<IOrderNotificationService>(MockBehavior.Strict);
        notifications.Setup(x => x.NotifyOrderCancelledToKitchen(1, 1, It.IsAny<string>()))
            .Returns(() =>
            {
                Assert.Null(db.Database.CurrentTransaction);
                if (++sends == 1) throw new InjectedFailure();
                return Task.CompletedTask;
            });
        var routes = Routes(db);
        var processor = Processor(db, Inventory(db), notifications.Object, routes.Object);
        await processor.ProcessPendingWebhookEventsAsync(default);
        db.ChangeTracker.Clear();
        var pending = await db.IntegrationWebhookEvents.SingleAsync(x => x.Id == 2);
        Assert.Equal("cancel_effects_pending", pending.Status);
        Assert.Equal("LOCAL_RAPPI_CANCEL_EFFECTS", pending.EventType);
        Assert.Contains("ready-test-order", pending.PayloadJson);
        Assert.Equal(OrderStatus.Cancelled, (await db.Orders.SingleAsync()).Status);

        db.ChangeTracker.Clear();
        await processor.ProcessPendingWebhookEventsAsync(default);
        await processor.ProcessPendingWebhookEventsAsync(default);
        await using var check = Db();
        Assert.Equal("processed", (await check.IntegrationWebhookEvents.SingleAsync(x => x.Id == 2)).Status);
        Assert.True((await check.AppPayments.SingleAsync()).IsReversed);
        Assert.Equal(2, sends);
        Assert.Single(await check.InventoryMovements.ToListAsync());
        routes.Verify(x => x.OnOrderCancelledWhileRouteOpenAsync(1, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        routes.Verify(x => x.TryFinalizeRouteWhenAllTerminalAsync(1, null, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [PostgreSqlIntegrationFact]
    public async Task Cancellation_waits_for_settlement_order_lock_and_reloads_previously_tracked_payments()
    {
        await using var setup = Db();
        await SeedAsync(setup);
        await using var cancelling = Db();
        await cancelling.Orders.LoadAsync();
        await cancelling.AppPayments.LoadAsync();
        await using var settling = Db();
        await using var transaction = await settling.Database.BeginTransactionAsync();
        await settling.Database.ExecuteSqlRawAsync("SELECT id FROM \"order\" WHERE id=1 AND tenant_id=1 FOR UPDATE");
        var actor = Actor();
        var branch = Branch();
        var command = new SettleMultipleAppPaymentsCommand { PaymentIds = [1], ActualAmount = 200 };
        var handler = new SettleMultipleAppPaymentsHandler(settling, actor, branch);
        var pipeline = new OrderMutationBehavior<SettleMultipleAppPaymentsCommand, bool>(settling, actor, branch,
            Mock.Of<IOrderNotificationService>(), NullLogger<OrderMutationBehavior<SettleMultipleAppPaymentsCommand, bool>>.Instance);
        await pipeline.Handle(command, token => handler.Handle(command, token), default);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var work = Invoke(Processor(cancelling, Inventory(cancelling)), "ApplyCancellationAsync", Cancel, timeout.Token);
        try { await Assert.ThrowsAsync<TimeoutException>(() => work.WaitAsync(TimeSpan.FromMilliseconds(150))); }
        finally { await transaction.CommitAsync(); }
        await work;

        await using var check = Db();
        Assert.Equal(OrderStatus.Taken, (await check.Orders.SingleAsync()).Status);
        var payment = await check.AppPayments.SingleAsync();
        Assert.True(payment.IsSetted);
        Assert.False(payment.IsReversed);
        Assert.Equal(ExternalOrderStatus.ReconciliationRequired, (await check.ExternalDeliveryOrders.SingleAsync()).Status);
        Assert.Equal(2, (await check.InventoryBalances.SingleAsync()).QuantityReserved);
        Assert.Empty(await check.InventoryMovements.ToListAsync());
    }

    [PostgreSqlIntegrationFact]
    public async Task Delayed_rejection_failure_cannot_overwrite_a_concurrent_cancellation_marker()
    {
        await using var db = Db();
        await SeedAsync(db);
        db.ExternalDeliveryOrders.Add(new ExternalDeliveryOrder { Id = 2, ConnectionId = 1, BranchId = 1,
            ExternalOrderId = "reject-race", Status = ExternalOrderStatus.BlockedMapping });
        await db.SaveChangesAsync();
        var provider = new Mock<IRappiDeliveryProvider>(MockBehavior.Strict);
        provider.Setup(x => x.RejectOrderAsync("reject-race", "test", It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                Assert.Null(db.Database.CurrentTransaction);
                await using var other = Db();
                await Invoke(Processor(other), "ApplyCancellationAsync", """{"order_id":"reject-race"}""");
                return new RappiOperationResult(false, 503, "synthetic timeout");
            });

        Assert.False((await Processor(db, provider: provider.Object).RejectAsync(2, "test", default)).Success);

        await using var check = Db();
        var marker = await check.ExternalDeliveryOrders.SingleAsync(x => x.Id == 2);
        Assert.Equal(ExternalOrderStatus.Cancelled, marker.Status);
        Assert.Null(marker.LastError);
        Assert.Null(marker.InternalOrderId);
    }

    [PostgreSqlIntegrationFact]
    public async Task Fake_provider_snapshot_is_ignored_and_server_derives_the_effects_context()
    {
        await using var db = Db();
        await SeedAsync(db);
        await AddCancelEventAsync(db,
            """{"order_id":"ready-test-order","reason":"test","snapshot":{"tenantId":99,"orderId":999,"routeId":999},"_effects":true}""");
        var notifications = new Mock<IOrderNotificationService>(MockBehavior.Strict);
        notifications.Setup(x => x.NotifyOrderCancelledToKitchen(1, 1, It.IsAny<string>())).Returns(Task.CompletedTask);
        var routes = Routes(db);
        await Processor(db, Inventory(db), notifications.Object, routes.Object).ProcessPendingWebhookEventsAsync(default);

        await using var check = Db();
        var row = await check.IntegrationWebhookEvents.SingleAsync(x => x.Id == 2);
        Assert.Equal("processed", row.Status);
        var envelope = JsonDocument.Parse(row.PayloadJson).RootElement;
        Assert.Equal(1, envelope.GetProperty("snapshot").GetProperty("tenantId").GetInt32());
        Assert.Equal(1, envelope.GetProperty("snapshot").GetProperty("orderId").GetInt32());
        Assert.Equal(JsonValueKind.Null, envelope.GetProperty("snapshot").GetProperty("routeId").ValueKind);
        Assert.Contains("999", envelope.GetProperty("originalPayload").GetString());
        routes.Verify(x => x.OnOrderCancelledWhileRouteOpenAsync(1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [PostgreSqlIntegrationFact]
    public async Task Lost_claim_before_effects_checkpoint_rolls_back_core_and_preserves_new_owner()
    {
        await using (var setup = Db()) { await SeedAsync(setup); await AddCancelEventAsync(setup); }
        var interceptor = new StealCancellationClaim(() => Db());
        await using var db = Db(interceptor: interceptor);
        await Processor(db, Inventory(db)).ProcessPendingWebhookEventsAsync(default);

        await using var check = Db();
        Assert.Equal(1, interceptor.Invocations);
        Assert.Equal(OrderStatus.Taken, (await check.Orders.SingleAsync()).Status);
        Assert.False((await check.AppPayments.SingleAsync()).IsReversed);
        Assert.Equal(2, (await check.InventoryBalances.SingleAsync()).QuantityReserved);
        Assert.Empty(await check.InventoryMovements.ToListAsync());
        var row = await check.IntegrationWebhookEvents.SingleAsync(x => x.Id == 2);
        Assert.Equal("ORDER_EVENT_CANCEL", row.EventType);
        Assert.Equal("processing_leased", row.Status);
        Assert.Equal(2, row.AttemptCount);
    }

    [PostgreSqlIntegrationFact]
    public void Effects_protocol_is_invisible_to_older_workers_but_recovers_and_fences_in_new_workers()
    {
        var pending = RappiWebhookRecoveryTests.Event(1, "cancel_effects_pending", 1, Now);
        var stale = RappiWebhookRecoveryTests.Event(2, "cancel_effects_leased", 2, Now.AddHours(-1));
        var active = RappiWebhookRecoveryTests.Event(3, "cancel_effects_leased", 2, Now);
        var exhausted = RappiWebhookRecoveryTests.Event(4, "cancel_effects_pending", 5, Now);
        var rows = new[] { pending, stale, active, exhausted }.AsQueryable();
        foreach (var row in rows) row.EventType = "LOCAL_RAPPI_CANCEL_EFFECTS";
        // Actual old worker predicates: received claims; processing_leased stale
        // unknown types are marked failed. Neither may see the new second phase.
        Assert.Empty(rows.Where(x => x.Status == "received"));
        Assert.Empty(rows.Where(x => x.Status == "processing_leased" && x.UpdatedAt < Now.AddMinutes(-15)));
        Assert.Equal([1, 2], RappiWebhookRecoveryTests.Query("ClaimableWebhookEvents", rows, Now).Select(x => x.Id));
        Assert.Equal([4], RappiWebhookRecoveryTests.Query("ExhaustedWebhookEvents", rows, Now).Select(x => x.Id));
        Assert.Empty(RappiWebhookRecoveryTests.Query("UncertainWebhookEvents", rows, Now));
        Assert.Single(RappiWebhookRecoveryTests.Query("OwnedWebhookClaim", rows, 2, 2));
        Assert.Empty(RappiWebhookRecoveryTests.Query("OwnedWebhookClaim", rows, 2, 1));
    }

    [PostgreSqlIntegrationFact]
    public async Task Retry_after_route_unlink_retains_original_route_and_delivery_notification_context()
    {
        await using var db = Db();
        await SeedAsync(db);
        await Inventory(db).ConsumeOrderAsync(await db.Orders.SingleAsync(), "test:handoff", default);
        db.DeliveryRoutes.Add(new DeliveryRoute { Id = 1, BranchId = 1, DeliverymanId = 1, LastAssignmentAtUtc = Now });
        var order = await db.Orders.SingleAsync();
        order.Status = OrderStatus.OnTheWay;
        order.DeliveryRouteId = 1;
        await db.SaveChangesAsync();
        await AddCancelEventAsync(db);
        var finalizations = 0;
        var routes = new Mock<IDeliveryRouteWorkflowService>(MockBehavior.Strict);
        routes.Setup(x => x.OnOrderCancelledWhileRouteOpenAsync(1, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                Assert.Null(db.Database.CurrentTransaction);
                (await db.Orders.SingleAsync()).DeliveryRouteId = null;
                await db.SaveChangesAsync();
            });
        routes.Setup(x => x.TryFinalizeRouteWhenAllTerminalAsync(1, 1, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                Assert.Null(db.Database.CurrentTransaction);
                if (++finalizations == 1) throw new InjectedFailure();
                return Task.CompletedTask;
            });
        var repository = new Mock<IOrderRepository>(MockBehavior.Strict);
        repository.Setup(x => x.GetByIdWithFullDetailsAsync(1, It.IsAny<CancellationToken>()))
            .Returns(async () => await db.Orders.SingleAsync());
        var mapper = new Mock<IMapper>(MockBehavior.Strict);
        mapper.Setup(x => x.Map<OrderDto>(It.IsAny<object>())).Returns(new OrderDto { Id = 1 });
        var notifications = new Mock<IOrderNotificationService>(MockBehavior.Strict);
        notifications.Setup(x => x.NotifyOrderModifiedToDelivery(It.Is<OrderDto>(x => x.Id == 1), "status", null))
            .Returns(Task.CompletedTask);
        var processor = Processor(db, Inventory(db), notifications.Object, routes.Object,
            repository: repository.Object, mapper: mapper.Object);
        await processor.ProcessPendingWebhookEventsAsync(default);
        db.ChangeTracker.Clear();
        Assert.Null((await db.Orders.SingleAsync()).DeliveryRouteId);
        var pending = await db.IntegrationWebhookEvents.SingleAsync(x => x.Id == 2);
        Assert.Equal(1, JsonDocument.Parse(pending.PayloadJson).RootElement
            .GetProperty("snapshot").GetProperty("routeId").GetInt32());
        db.ChangeTracker.Clear();
        await processor.ProcessPendingWebhookEventsAsync(default);
        Assert.Equal(2, finalizations);
        Assert.Equal("processed", (await db.IntegrationWebhookEvents.AsNoTracking().SingleAsync(x => x.Id == 2)).Status);
        notifications.Verify(x => x.NotifyOrderModifiedToDelivery(It.IsAny<OrderDto>(), "status", null), Times.Once);
    }

    private static async Task SeedAsync(ApplicationDbContext db)
    {
        await RappiReadyTestData.SeedAsync(db, orderStatus: OrderStatus.Taken);
        db.AddRange(new ProductCategory { Id = 1, BranchId = 1, Name = "Synthetic drinks" },
            new Product { Id = 1, CategoryId = 1, Name = "Synthetic water", Price = 100,
                InventoryEnabled = true, InventoryControlMode = InventoryControlMode.Strict },
            new ExpenseCategory { Id = 1, Name = "Synthetic stock" },
            new Expense { Id = 1, CategoryId = 1, Name = "Synthetic bottle", TracksInventory = true, InventoryActive = true },
            new InventoryBalance { Id = 1, BranchId = 1, ExpenseId = 1, QuantityOnHand = 10, QuantityReserved = 2 },
            new OrderInventoryAllocation { Id = 1, OrderId = 1, ProductId = 1, ExpenseId = 1,
                ControlMode = InventoryControlMode.Strict, BaseQuantity = 2, ReservedQuantity = 2 },
            new AppPayment { Id = 1, OrderId = 1, AppId = 1, Amount = 200 });
        await db.SaveChangesAsync();
        // Explicit fixture IDs do not advance PostgreSQL identity sequences.
        await db.Database.ExecuteSqlRawAsync("SELECT setval(pg_get_serial_sequence('external_delivery_order', 'id'), 1, true)");
        db.ChangeTracker.Clear();
    }

    private static async Task AddCancelEventAsync(ApplicationDbContext db, string? payload = null)
    {
        db.IntegrationWebhookEvents.Add(new IntegrationWebhookEvent { Id = 2, ConnectionId = 1, Provider = "rappi",
            EventKey = "cancel-regression", EventType = "ORDER_EVENT_CANCEL", PayloadJson = payload ?? Cancel,
            Status = "received", CreatedAt = Now, UpdatedAt = Now });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static ICurrentUser Actor()
    {
        var actor = new Mock<ICurrentUser>();
        actor.SetupGet(x => x.Id).Returns(1); actor.SetupGet(x => x.TenantId).Returns(1);
        actor.SetupGet(x => x.Role).Returns("cashier"); actor.SetupGet(x => x.BranchId).Returns(1);
        actor.SetupGet(x => x.IsAuthenticated).Returns(true);
        return actor.Object;
    }

    private static IBranchContext Branch()
    {
        var branch = new Mock<IBranchContext>();
        branch.SetupGet(x => x.EffectiveBranchId).Returns(1);
        return branch.Object;
    }

    private static InventoryService Inventory(ApplicationDbContext db) =>
        new(db, Actor(), new TestTenantContext(), new FakeClock(Now), Branch());

    private static Mock<IDeliveryRouteWorkflowService> Routes(ApplicationDbContext db)
    {
        var routes = new Mock<IDeliveryRouteWorkflowService>(MockBehavior.Strict);
        routes.Setup(x => x.OnOrderCancelledWhileRouteOpenAsync(1, It.IsAny<CancellationToken>()))
            .Returns(() => { Assert.Null(db.Database.CurrentTransaction); return Task.CompletedTask; });
        routes.Setup(x => x.TryFinalizeRouteWhenAllTerminalAsync(1, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(() => { Assert.Null(db.Database.CurrentTransaction); return Task.CompletedTask; });
        return routes;
    }

    private static RappiOrderProcessor Processor(ApplicationDbContext db, IInventoryService? inventory = null,
        IOrderNotificationService? notifications = null, IDeliveryRouteWorkflowService? routes = null,
        IRappiDeliveryProvider? provider = null, IOrderRepository? repository = null, IMapper? mapper = null) => new(db,
            provider ?? Mock.Of<IRappiDeliveryProvider>(MockBehavior.Strict), new FakeClock(Now),
            repository ?? Mock.Of<IOrderRepository>(MockBehavior.Strict), mapper ?? Mock.Of<IMapper>(MockBehavior.Strict),
            notifications ?? Mock.Of<IOrderNotificationService>(MockBehavior.Strict),
            routes ?? Mock.Of<IDeliveryRouteWorkflowService>(MockBehavior.Strict),
            NullLogger<RappiOrderProcessor>.Instance, inventory: inventory);

    private static Task Invoke(RappiOrderProcessor processor, string method, string payload, CancellationToken ct = default) =>
        (Task)typeof(RappiOrderProcessor).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(processor, [1, payload, ct])!;

    private sealed class InjectedFailure : Exception;

    private sealed class StealCancellationClaim(Func<ApplicationDbContext> createDb) : DbCommandInterceptor
    {
        private int invoked;
        public int Invocations => invoked;
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("UPDATE", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("integration_webhook_event", StringComparison.Ordinal)
                && command.CommandText.Contains("payload_json =", StringComparison.Ordinal)
                && command.CommandText.Contains("LOCAL_RAPPI_CANCEL_EFFECTS", StringComparison.Ordinal)
                && Interlocked.CompareExchange(ref invoked, 1, 0) == 0)
            {
                await using var competitor = createDb();
                var stolen = await competitor.IntegrationWebhookEvents.Where(x => x.Id == 2 && x.AttemptCount == 1)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.AttemptCount, 2), cancellationToken);
                Assert.Equal(1, stolen);
            }
            return result;
        }
    }
}
