using System.Reflection;
using System.Data.Common;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Integrations;
using Testcontainers.PostgreSql;

namespace SenorArroz.Tests;

public sealed class RappiWebhookRecoveryTests
{
    internal static readonly DateTime Now = new(2026, 10, 21, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Claim_selection_recovers_abandoned_processing_but_not_active_or_terminal_rows()
    {
        var rows = new[]
        {
            Event(1, "received", 0, Now),
            Event(2, "processing_leased", 1, Now.AddMinutes(-16)),
            Event(3, "processing_leased", 1, Now.AddMinutes(-14)),
            Event(4, "processed", 1, Now.AddHours(-1)),
            Event(5, "failed", 5, Now.AddHours(-1)),
            Event(6, "outbox_pending", 1, Now.AddHours(-1)),
            Event(7, "received", 5, Now.AddHours(-1)),
            Event(8, "processing_leased", 5, Now.AddHours(-1))
        }.AsQueryable();

        Assert.Equal([1, 2], Query("ClaimableWebhookEvents", rows, Now).Select(x => x.Id));
        Assert.Equal([7, 8], Query("ExhaustedWebhookEvents", rows, Now).Select(x => x.Id));
    }

    [Fact]
    public void Exhausted_active_claim_is_not_failed_until_its_lease_expires()
    {
        var row = Event(1, "processing_leased", 5, Now.AddMinutes(-14));
        Assert.Empty(Query("ExhaustedWebhookEvents", new[] { row }.AsQueryable(), Now));
        row.UpdatedAt = Now.AddMinutes(-15);
        Assert.Single(Query("ExhaustedWebhookEvents", new[] { row }.AsQueryable(), Now));
    }

    [Fact]
    public void Fencing_does_not_allow_an_old_worker_to_complete_a_reclaimed_event()
    {
        var rows = new[] { Event(1, "processing_leased", 3, Now) }.AsQueryable();
        Assert.Empty(Query("OwnedWebhookClaim", rows, 1, 2));
        Assert.Single(Query("OwnedWebhookClaim", rows, 1, 3));
    }

    [Fact]
    public void Other_providers_and_outbox_rows_are_never_recovered_by_the_inbox()
    {
        var other = Event(1, "processing_leased", 1, Now.AddHours(-1));
        other.Provider = "another-provider";
        var outbox = Event(2, "outbox_pending", 10, Now.AddHours(-1));
        var rows = new[] { other, outbox }.AsQueryable();
        Assert.Empty(Query("ClaimableWebhookEvents", rows, Now));
        Assert.Empty(Query("ExhaustedWebhookEvents", rows, Now));
    }

    [Theory]
    [InlineData(1, -60)]
    [InlineData(5, -60)]
    [InlineData(1, -1)]
    [InlineData(5, -1)]
    public void Legacy_processing_is_never_replayed_failed_or_owned_by_the_leased_protocol(int attempts, int ageMinutes)
    {
        var rows = new[] { Event(1, "processing", attempts, Now.AddMinutes(ageMinutes)) }.AsQueryable();
        Assert.Empty(Query("ClaimableWebhookEvents", rows, Now));
        Assert.Empty(Query("ExhaustedWebhookEvents", rows, Now));
        Assert.Empty(Query("UncertainWebhookEvents", rows, Now));
        Assert.Empty(Query("OwnedWebhookClaim", rows, 1, attempts));
    }

    [Theory]
    [InlineData(ExternalOrderStatus.Cancelled, false)]
    [InlineData(ExternalOrderStatus.Rejected, false)]
    [InlineData(ExternalOrderStatus.Expired, false)]
    [InlineData(ExternalOrderStatus.ReconciliationRequired, false)]
    [InlineData(ExternalOrderStatus.Cancelled, true)]
    [InlineData(ExternalOrderStatus.Rejected, true)]
    [InlineData(ExternalOrderStatus.Expired, true)]
    [InlineData(ExternalOrderStatus.ReconciliationRequired, true)]
    public async Task Terminal_external_order_without_internal_order_is_never_resurrected(
        ExternalOrderStatus terminalStatus, bool revalidate)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ApplicationDbContext(options,
            currentTenant: TestTenantContext.Default, tenantExecutionContext: TestTenantContext.Default);
        var connection = new DeliveryAppConnection { Id = 999, BranchId = 1, Provider = "rappi" };
        var external = new ExternalDeliveryOrder
        {
            Id = 1, ConnectionId = 999, Connection = connection, BranchId = 1,
            ExternalOrderId = "terminal-test", Status = terminalStatus,
            RawPayloadJson = "{\"historical\":true}", LastError = "Preserve historical reason",
            UpdatedAt = Now.AddHours(-1)
        };
        db.ExternalDeliveryOrders.Add(external);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var provider = new Mock<IRappiDeliveryProvider>(MockBehavior.Strict);
        var inventory = new Mock<IInventoryService>(MockBehavior.Strict);
        var notifications = new Mock<IOrderNotificationService>(MockBehavior.Strict);
        var routes = new Mock<IDeliveryRouteWorkflowService>(MockBehavior.Strict);
        var print = new Mock<IPrintQueueService>(MockBehavior.Strict);
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(Now);
        var processor = new RappiOrderProcessor(db, provider.Object, clock.Object,
            Mock.Of<IOrderRepository>(), Mock.Of<IMapper>(), notifications.Object,
            routes.Object, NullLogger<RappiOrderProcessor>.Instance, print.Object, inventory.Object);

        var result = revalidate
            ? await processor.RevalidateAndAcceptAsync(1, null, CancellationToken.None)
            : await processor.IngestNewOrderAsync(999, "{\"order_id\":\"terminal-test\"}", CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(result.Held);
        Assert.Equal(1, result.ExternalOrderId);
        Assert.Null(result.InternalOrderId);
        Assert.Contains("no puede aceptarse de nuevo", result.Error);
        var persisted = await db.ExternalDeliveryOrders.AsNoTracking().SingleAsync();
        Assert.Equal(terminalStatus, persisted.Status);
        Assert.Null(persisted.InternalOrderId);
        Assert.Equal("{\"historical\":true}", persisted.RawPayloadJson);
        Assert.Equal("Preserve historical reason", persisted.LastError);
        Assert.Equal(Now.AddHours(-1), persisted.UpdatedAt);
        Assert.False(db.ChangeTracker.HasChanges());
        Assert.Empty(await db.Orders.ToListAsync());
        Assert.Empty(await db.AppPayments.ToListAsync());
        provider.VerifyNoOtherCalls();
        inventory.VerifyNoOtherCalls();
        notifications.VerifyNoOtherCalls();
        routes.VerifyNoOtherCalls();
        print.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("MENU_APPROVED")]
    [InlineData("MENU_REJECTED")]
    [InlineData("STORE_CONNECTIVITY")]
    public void Interrupted_non_idempotent_events_require_review_but_fresh_received_events_still_process(string eventType)
    {
        var stale = Event(1, "processing_leased", 1, Now.AddHours(-1));
        stale.EventType = eventType;
        var fresh = Event(2, "received", 0, Now);
        fresh.EventType = eventType;
        var rows = new[] { stale, fresh }.AsQueryable();
        Assert.Equal([2], Query("ClaimableWebhookEvents", rows, Now).Select(x => x.Id));
        Assert.Equal([1], Query("UncertainWebhookEvents", rows, Now).Select(x => x.Id));
    }

    [Theory]
    [InlineData("hand_to_domiciliary", OrderStatus.OnTheWay)]
    [InlineData("close_order", OrderStatus.Delivered)]
    public async Task Replayed_order_state_event_does_not_overwrite_historical_transition_time(string eventName, OrderStatus status)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ApplicationDbContext(options,
            currentTenant: TestTenantContext.Default, tenantExecutionContext: TestTenantContext.Default);
        var order = new Order { Id = 1, BranchId = 1, TakenById = 1, Type = OrderType.Delivery, Status = status };
        order.AddStatusTime(status, Now.AddHours(-1));
        var originalTimes = order.StatusTimes;
        db.ExternalDeliveryOrders.Add(new ExternalDeliveryOrder
        {
            Id = 1, ConnectionId = 999, BranchId = 1, ExternalOrderId = "replayed-test",
            InternalOrderId = 1, InternalOrder = order
        });
        await db.SaveChangesAsync();
        var inventory = new Mock<IInventoryService>(MockBehavior.Strict);
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(Now);
        var processor = new RappiOrderProcessor(db, Mock.Of<IRappiDeliveryProvider>(), clock.Object,
            Mock.Of<IOrderRepository>(), Mock.Of<IMapper>(), Mock.Of<IOrderNotificationService>(),
            Mock.Of<IDeliveryRouteWorkflowService>(), NullLogger<RappiOrderProcessor>.Instance,
            inventory: inventory.Object);
        var method = typeof(RappiOrderProcessor).GetMethod("ApplyOrderEventAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var payload = $$"""{"order_id":"replayed-test","event":"{{eventName}}"}""";
        await (Task)method.Invoke(processor, [999, payload, CancellationToken.None])!;
        Assert.Equal(status, order.Status);
        Assert.Equal(originalTimes, order.StatusTimes);
        inventory.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Recovery_queries_preserve_the_current_tenant_filter()
    {
        var tenant = new TestTenantContext();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ApplicationDbContext(options, currentTenant: tenant, tenantExecutionContext: tenant);
        using (tenant.BeginSystemScope())
        {
            db.IntegrationWebhookEvents.Add(Event(1, "processing_leased", 1, Now.AddHours(-1)));
            var foreign = Event(2, "processing_leased", 1, Now.AddHours(-1));
            foreign.TenantId = 2;
            db.IntegrationWebhookEvents.Add(foreign);
            await db.SaveChangesAsync();
        }

        var ids = await Query("ClaimableWebhookEvents", db.IntegrationWebhookEvents, Now)
            .Select(x => x.Id).ToListAsync();
        Assert.Equal([1], ids);
    }

    internal static IntegrationWebhookEvent Event(int id, string status, int attempts, DateTime updatedAt) => new()
    {
        Id = id,
        TenantId = 1,
        ConnectionId = 999,
        Provider = "rappi",
        EventKey = $"recovery-test-{id}",
        EventType = "ORDER_EVENT_CANCEL",
        PayloadJson = "{}",
        Status = status,
        AttemptCount = attempts,
        CreatedAt = Now.AddHours(-2),
        UpdatedAt = updatedAt
    };

    internal static IQueryable<IntegrationWebhookEvent> Query(
        string name, IQueryable<IntegrationWebhookEvent> source, params object[] args)
    {
        var method = typeof(RappiOrderProcessor).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing recovery query: {name}");
        return (IQueryable<IntegrationWebhookEvent>)method.Invoke(null, new object[] { source }.Concat(args).ToArray())!;
    }
}

/// <summary>Only a disposable local PostgreSQL database; never starts the API or calls a provider.</summary>
public sealed class RappiWebhookRecoveryPostgreSqlTests
{
    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Abandoned_claim_retries_and_exhausted_claim_is_visible_without_reclaiming_active_work()
    {
        await using var fixture = await IsolatedDatabase.CreateAsync();
        await using var db = fixture.Db();
        db.IntegrationWebhookEvents.AddRange(
            RappiWebhookRecoveryTests.Event(1, "processing_leased", 1, RappiWebhookRecoveryTests.Now.AddMinutes(-16)),
            RappiWebhookRecoveryTests.Event(2, "processing_leased", 1, RappiWebhookRecoveryTests.Now.AddMinutes(-14)),
            RappiWebhookRecoveryTests.Event(3, "processing_leased", 5, RappiWebhookRecoveryTests.Now.AddHours(-1)),
            RappiWebhookRecoveryTests.Event(4, "outbox_pending", 10, RappiWebhookRecoveryTests.Now.AddHours(-1)));
        var menu = RappiWebhookRecoveryTests.Event(5, "processing_leased", 1, RappiWebhookRecoveryTests.Now.AddHours(-1));
        menu.EventType = "MENU_APPROVED";
        var connectivity = RappiWebhookRecoveryTests.Event(6, "processing_leased", 1, RappiWebhookRecoveryTests.Now.AddHours(-1));
        connectivity.EventType = "STORE_CONNECTIVITY";
        connectivity.PayloadJson = """{"store_id":"isolated-test-store","online":true}""";
        db.IntegrationWebhookEvents.AddRange(menu, connectivity);
        db.IntegrationWebhookEvents.AddRange(
            RappiWebhookRecoveryTests.Event(7, "processing", 1, RappiWebhookRecoveryTests.Now.AddHours(-1)),
            RappiWebhookRecoveryTests.Event(8, "processing", 5, RappiWebhookRecoveryTests.Now.AddHours(-1)),
            RappiWebhookRecoveryTests.Event(9, "processing", 1, RappiWebhookRecoveryTests.Now.AddMinutes(-1)),
            RappiWebhookRecoveryTests.Event(10, "processing", 5, RappiWebhookRecoveryTests.Now.AddMinutes(-1)));
        db.RappiMenuPublications.Add(new RappiMenuPublication
        {
            ConnectionId = 999, StoreId = "isolated-test-store", Status = "submitted",
            PayloadJson = "{}", CreatedAt = RappiWebhookRecoveryTests.Now
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var provider = new Mock<IRappiDeliveryProvider>(MockBehavior.Strict);
        var processor = new RappiOrderProcessor(db, provider.Object, new RecoveryClock(),
            Mock.Of<IOrderRepository>(), Mock.Of<IMapper>(), Mock.Of<IOrderNotificationService>(),
            Mock.Of<IDeliveryRouteWorkflowService>(), NullLogger<RappiOrderProcessor>.Instance);
        // The fixture has a valid connection but this deliberately malformed cancellation
        // has no order_id. It fails locally before any order/payment/inventory operation.
        await processor.ProcessPendingWebhookEventsAsync(CancellationToken.None);

        var rows = await db.IntegrationWebhookEvents.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        Assert.Equal("received", rows[0].Status);
        Assert.Equal(2, rows[0].AttemptCount);
        Assert.Equal(RappiWebhookRecoveryTests.Now, rows[0].UpdatedAt);
        Assert.Equal("processing_leased", rows[1].Status);
        Assert.Equal(1, rows[1].AttemptCount);
        Assert.Equal("failed", rows[2].Status);
        Assert.Contains("agotaron", rows[2].LastError);
        Assert.Equal("outbox_pending", rows[3].Status);
        Assert.All(rows.Skip(4).Take(2), row =>
        {
            Assert.Equal("failed", row.Status);
            Assert.Contains("incierto", row.LastError);
            Assert.Equal(1, row.AttemptCount);
        });
        Assert.Equal("submitted", (await db.RappiMenuPublications.AsNoTracking().SingleAsync()).Status);
        Assert.False((await db.DeliveryAppStores.AsNoTracking().SingleAsync()).ConnectivityEnabled);
        foreach (var legacy in rows.Where(x => x.Id >= 7))
        {
            Assert.Equal("processing", legacy.Status);
            Assert.Equal(legacy.Id % 2 == 0 ? 5 : 1, legacy.AttemptCount);
            Assert.Equal(legacy.Id <= 8 ? RappiWebhookRecoveryTests.Now.AddHours(-1)
                : RappiWebhookRecoveryTests.Now.AddMinutes(-1), legacy.UpdatedAt);
            Assert.Null(legacy.LastError);
        }
        provider.VerifyNoOtherCalls();
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Fresh_connectivity_business_change_and_inbox_completion_commit_together()
    {
        await using var fixture = await IsolatedDatabase.CreateAsync();
        await using var db = fixture.Db();
        var webhook = RappiWebhookRecoveryTests.Event(1, "received", 0, RappiWebhookRecoveryTests.Now);
        webhook.EventType = "STORE_CONNECTIVITY";
        webhook.PayloadJson = """{"store_id":"isolated-test-store","online":true}""";
        db.IntegrationWebhookEvents.Add(webhook);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var provider = new Mock<IRappiDeliveryProvider>(MockBehavior.Strict);
        var processor = new RappiOrderProcessor(db, provider.Object, new RecoveryClock(),
            Mock.Of<IOrderRepository>(), Mock.Of<IMapper>(), Mock.Of<IOrderNotificationService>(),
            Mock.Of<IDeliveryRouteWorkflowService>(), NullLogger<RappiOrderProcessor>.Instance);

        await processor.ProcessPendingWebhookEventsAsync(CancellationToken.None);

        var persisted = await db.IntegrationWebhookEvents.AsNoTracking().SingleAsync();
        var store = await db.DeliveryAppStores.AsNoTracking().SingleAsync();
        Assert.Equal("processed", persisted.Status);
        Assert.Equal(RappiWebhookRecoveryTests.Now, persisted.ProcessedAt);
        Assert.True(store.ConnectivityEnabled);
        Assert.Equal(RappiWebhookRecoveryTests.Now, store.LastConnectivityAt);
        provider.VerifyNoOtherCalls();
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Lost_claim_rolls_back_pending_business_changes_without_overwriting_new_owner()
    {
        await using var fixture = await IsolatedDatabase.CreateAsync();
        await using (var seed = fixture.Db())
        {
            var webhook = RappiWebhookRecoveryTests.Event(1, "received", 0, RappiWebhookRecoveryTests.Now);
            webhook.EventType = "STORE_CONNECTIVITY";
            webhook.PayloadJson = """{"store_id":"isolated-test-store","online":true}""";
            seed.IntegrationWebhookEvents.Add(webhook);
            await seed.SaveChangesAsync();
        }
        var interceptor = new ClaimStealingInterceptor(() => fixture.Db());
        await using var db = fixture.Db(interceptor: interceptor);
        var processor = new RappiOrderProcessor(db, Mock.Of<IRappiDeliveryProvider>(), new RecoveryClock(),
            Mock.Of<IOrderRepository>(), Mock.Of<IMapper>(), Mock.Of<IOrderNotificationService>(),
            Mock.Of<IDeliveryRouteWorkflowService>(), NullLogger<RappiOrderProcessor>.Instance);

        await processor.ProcessPendingWebhookEventsAsync(CancellationToken.None);

        await using var observed = fixture.Db();
        Assert.Equal(1, interceptor.Invocations);
        Assert.False((await observed.DeliveryAppStores.AsNoTracking().SingleAsync()).ConnectivityEnabled);
        var persisted = await observed.IntegrationWebhookEvents.AsNoTracking().SingleAsync();
        Assert.Equal("processing_leased", persisted.Status);
        Assert.Equal(2, persisted.AttemptCount);
        Assert.Null(persisted.ProcessedAt);
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Concurrent_claims_have_one_winner_and_old_attempt_cannot_complete()
    {
        await using var fixture = await IsolatedDatabase.CreateAsync();
        await using (var seed = fixture.Db())
        {
            seed.IntegrationWebhookEvents.Add(RappiWebhookRecoveryTests.Event(
                1, "processing_leased", 1, RappiWebhookRecoveryTests.Now.AddHours(-1)));
            await seed.SaveChangesAsync();
        }
        await using var first = fixture.Db();
        await using var second = fixture.Db();
        async Task<int> Claim(ApplicationDbContext db) => await RappiWebhookRecoveryTests.Query(
                "ClaimableWebhookEvents", db.IntegrationWebhookEvents, RappiWebhookRecoveryTests.Now)
            .Where(x => x.Id == 1 && x.AttemptCount == 1)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, "processing_leased")
                .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1)
                .SetProperty(x => x.UpdatedAt, RappiWebhookRecoveryTests.Now));

        var results = await Task.WhenAll(Claim(first), Claim(second));
        Assert.Equal(1, results.Sum());
        var staleCompletion = await RappiWebhookRecoveryTests.Query(
                "OwnedWebhookClaim", first.IntegrationWebhookEvents, 1, 1)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, "processed"));
        Assert.Equal(0, staleCompletion);
        var row = await first.IntegrationWebhookEvents.AsNoTracking().SingleAsync();
        Assert.Equal("processing_leased", row.Status);
        Assert.Equal(2, row.AttemptCount);
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Rolling_workers_claim_received_rows_exclusively_across_protocol_versions()
    {
        await using var fixture = await IsolatedDatabase.CreateAsync();
        await using var oldWorker = fixture.Db();
        await using var newWorker = fixture.Db();
        oldWorker.IntegrationWebhookEvents.AddRange(
            RappiWebhookRecoveryTests.Event(1, "received", 0, RappiWebhookRecoveryTests.Now.AddHours(-1)),
            RappiWebhookRecoveryTests.Event(2, "received", 0, RappiWebhookRecoveryTests.Now.AddHours(-1)));
        await oldWorker.SaveChangesAsync();
        oldWorker.ChangeTracker.Clear();

        Task<int> OldClaim(int id) => oldWorker.IntegrationWebhookEvents
            .Where(x => x.Id == id && x.Provider == "rappi" && x.Status == "received")
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, "processing")
                .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1));
        Task<int> NewClaim(int id) => RappiWebhookRecoveryTests.Query("ClaimableWebhookEvents",
                newWorker.IntegrationWebhookEvents, RappiWebhookRecoveryTests.Now)
            .Where(x => x.Id == id && x.AttemptCount == 0)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, "processing_leased")
                .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1)
                .SetProperty(x => x.UpdatedAt, RappiWebhookRecoveryTests.Now));

        // Both rollout orderings: an old worker wins row 1, a new worker wins row 2.
        Assert.Equal(1, await OldClaim(1));
        Assert.Equal(0, await NewClaim(1));
        Assert.Equal(1, await NewClaim(2));
        Assert.Equal(0, await OldClaim(2));
        Assert.Equal(0, await RappiWebhookRecoveryTests.Query("OwnedWebhookClaim",
                newWorker.IntegrationWebhookEvents, 1, 1)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, "processed")));
        // Legacy completion used tracked SaveChanges without an ownership fence.
        // Safety therefore depends on never reclaiming its `processing` rows and
        // the failed old claim above: it never owns/loads the leased row to complete.

        var legacy = await oldWorker.IntegrationWebhookEvents.AsNoTracking().SingleAsync(x => x.Id == 1);
        Assert.Equal("processing", legacy.Status);
        Assert.Equal(RappiWebhookRecoveryTests.Now.AddHours(-1), legacy.UpdatedAt);
        Assert.Equal("processing_leased", (await newWorker.IntegrationWebhookEvents.AsNoTracking()
            .SingleAsync(x => x.Id == 2)).Status);
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Heartbeat_uses_independent_tenant_scope_and_does_not_renew_foreign_or_reclaimed_work()
    {
        await using var fixture = await IsolatedDatabase.CreateAsync();
        await using var db = fixture.Db();
        using (TestTenantContext.Default.BeginSystemScope())
        {
            db.Tenants.Add(new Tenant { Id = 2, Name = "Foreign test", Slug = "foreign-test" });
            db.IntegrationWebhookEvents.Add(RappiWebhookRecoveryTests.Event(
                1, "processing_leased", 2, RappiWebhookRecoveryTests.Now.AddMinutes(-14)));
            var foreign = RappiWebhookRecoveryTests.Event(
                2, "processing_leased", 2, RappiWebhookRecoveryTests.Now.AddMinutes(-14));
            foreign.TenantId = 2;
            db.IntegrationWebhookEvents.Add(foreign);
            await db.SaveChangesAsync();
        }
        db.ChangeTracker.Clear();
        var contextCount = 0;
        await using var services = new ServiceCollection()
            .AddScoped<TestTenantContext>()
            .AddScoped<ITenantExecutionContext>(sp => sp.GetRequiredService<TestTenantContext>())
            .AddScoped<IApplicationDbContext>(sp =>
            {
                Interlocked.Increment(ref contextCount);
                return fixture.Db(sp.GetRequiredService<TestTenantContext>());
            })
            .BuildServiceProvider();
        var processor = new RappiOrderProcessor(db, Mock.Of<IRappiDeliveryProvider>(), new RecoveryClock(),
            Mock.Of<IOrderRepository>(), Mock.Of<IMapper>(), Mock.Of<IOrderNotificationService>(),
            Mock.Of<IDeliveryRouteWorkflowService>(), NullLogger<RappiOrderProcessor>.Instance,
            serviceScopeFactory: services.GetRequiredService<IServiceScopeFactory>());
        var method = typeof(RappiOrderProcessor).GetMethod("RenewWebhookLeaseAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Task<bool> Renew(int id, int attempt) => (Task<bool>)method.Invoke(
            processor, [id, attempt, 1, CancellationToken.None])!;

        Assert.True(await Renew(1, 2));
        Assert.False(await Renew(1, 1));
        Assert.False(await Renew(2, 2));
        Assert.Equal(3, contextCount);
        Assert.Empty(await RappiWebhookRecoveryTests.Query("ClaimableWebhookEvents",
                db.IntegrationWebhookEvents, RappiWebhookRecoveryTests.Now.AddMinutes(14)).ToListAsync());
        var row = await db.IntegrationWebhookEvents.AsNoTracking().SingleAsync();
        Assert.Equal(RappiWebhookRecoveryTests.Now, row.UpdatedAt);
        Assert.Equal(2, row.AttemptCount);
        using (TestTenantContext.Default.BeginSystemScope())
        {
            var untouched = await db.IntegrationWebhookEvents.AsNoTracking().SingleAsync(x => x.Id == 2);
            Assert.Equal(RappiWebhookRecoveryTests.Now.AddMinutes(-14), untouched.UpdatedAt);
        }
    }

    private sealed class RecoveryClock : IClock
    {
        public DateTime UtcNow => RappiWebhookRecoveryTests.Now;
    }

    private sealed class ClaimStealingInterceptor(Func<ApplicationDbContext> createDb) : DbCommandInterceptor
    {
        private int invoked;
        public int Invocations => invoked;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("UPDATE", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("delivery_app_store", StringComparison.OrdinalIgnoreCase)
                && Interlocked.CompareExchange(ref invoked, 1, 0) == 0)
            {
                // A separate context takes ownership just before the pending business UPDATE.
                // The original transaction has not locked the event row, so the new claim
                // commits independently and must survive the original worker's rollback.
                await using var competing = createDb();
                await competing.IntegrationWebhookEvents.Where(x => x.Id == 1 && x.AttemptCount == 1)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.AttemptCount, 2)
                        .SetProperty(x => x.UpdatedAt, RappiWebhookRecoveryTests.Now), cancellationToken);
            }
            return result;
        }
    }

    private sealed class IsolatedDatabase : IAsyncDisposable
    {
        private PostgreSqlContainer? postgres;
        private string? serverConnectionString;
        private string? databaseName;
        private DbContextOptions<ApplicationDbContext> options = null!;

        public ApplicationDbContext Db(TestTenantContext? tenant = null, IInterceptor? interceptor = null) => new(
            interceptor is null ? options : new DbContextOptionsBuilder<ApplicationDbContext>(options)
                .AddInterceptors(interceptor).Options,
            currentTenant: tenant ?? TestTenantContext.Default,
            tenantExecutionContext: tenant ?? TestTenantContext.Default);

        public static async Task<IsolatedDatabase> CreateAsync()
        {
            var result = new IsolatedDatabase();
            try
            {
                var supplied = Environment.GetEnvironmentVariable("RAPPI_POSTGRES_TEST_CONNECTION");
                string connectionString;
                if (!string.IsNullOrWhiteSpace(supplied))
                {
                    var builder = new NpgsqlConnectionStringBuilder(supplied);
                    if (builder.Host is not ("localhost" or "127.0.0.1" or "::1")
                        || builder.Database is null
                        || (builder.Database != "postgres" && !builder.Database.Contains("test", StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidOperationException("Rappi recovery tests require a loopback PostgreSQL test server.");
                    result.databaseName = $"rappi_webhook_test_{Guid.NewGuid():N}";
                    builder.Database = "postgres";
                    builder.Pooling = false;
                    result.serverConnectionString = builder.ConnectionString;
                    await using var server = new NpgsqlConnection(result.serverConnectionString);
                    await server.OpenAsync();
                    await new NpgsqlCommand($"CREATE DATABASE {result.databaseName}", server).ExecuteNonQueryAsync();
                    builder.Database = result.databaseName;
                    connectionString = builder.ConnectionString;
                }
                else
                {
                    result.postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
                    await result.postgres.StartAsync();
                    connectionString = result.postgres.GetConnectionString();
                }
                result.options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options;
                await using var db = result.Db();
                await db.Database.EnsureCreatedAsync();
                db.Tenants.Add(new Tenant { Id = 1, Name = "Rappi recovery test", Slug = "rappi-recovery-test" });
                var branch = new Branch { Id = 1, Name = "Isolated test", Address = "Test", Phone1 = "000" };
                var bank = new Bank { Id = 1, Branch = branch, BranchId = 1, Name = "Test bank" };
                var app = new App { Id = 1, Bank = bank, BankId = 1, Name = "Test app" };
                var connection = new DeliveryAppConnection
                {
                    Id = 999,
                    Branch = branch,
                    BranchId = 1,
                    FinancialApp = app,
                    FinancialAppId = 1,
                    Provider = "rappi",
                    IsActive = true,
                    IsVerified = true
                };
                connection.Stores.Add(new DeliveryAppStore
                {
                    Id = 1, RappiStoreId = "isolated-test-store", StoreIntegrationId = "isolated-test-store",
                    Name = "Isolated test store", IsParent = true, ConnectivityEnabled = false
                });
                db.DeliveryAppConnections.Add(connection);
                await db.SaveChangesAsync();
                return result;
            }
            catch
            {
                await result.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (serverConnectionString is not null && databaseName is not null)
            {
                await using var server = new NpgsqlConnection(serverConnectionString);
                await server.OpenAsync();
                await new NpgsqlCommand($"DROP DATABASE IF EXISTS {databaseName} WITH (FORCE)", server).ExecuteNonQueryAsync();
            }
            if (postgres is not null)
                await postgres.DisposeAsync();
        }
    }
}
