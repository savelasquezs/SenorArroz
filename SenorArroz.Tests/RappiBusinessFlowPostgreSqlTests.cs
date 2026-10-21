using System.Reflection;
using System.Data.Common;
using System.Net;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using AutoMapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SenorArroz.API.Controllers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Features.Inventory.Services;
using SenorArroz.Application.Features.Orders.Commands;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Integrations;
using SenorArroz.Infrastructure.Repositories;
using SenorArroz.Infrastructure.Services;

namespace SenorArroz.Tests;

/// <summary>Real PostgreSQL persistence, inventory and print queue; provider/transports and DTO mapping are simulated. Not provider homologation.</summary>
public sealed class RappiBusinessFlowPostgreSqlTests
{
    [PostgreSqlIntegrationFact]
    public async Task Accept_winning_external_order_lock_is_then_cancelled_without_open_reservation()
    {
        await using var fixture = await RappiIsolatedBusinessDatabase.CreateAsync();
        await using var acceptingDb = fixture.Db();
        var cancellationLock = new AdvisoryLockProbe();
        await using var cancellingDb = fixture.Db(interceptor: cancellationLock);
        var accepting = new Flow(acceptingDb, 1, 11);
        var cancelling = new Flow(cancellingDb, 1, 11);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var providerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProvider = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        accepting.Provider.Setup(x => x.AcceptOrderAsync("accept-wins", 25, It.IsAny<CancellationToken>()))
            .Returns(async (string _, int _, CancellationToken ct) =>
            {
                providerEntered.TrySetResult();
                await releaseProvider.Task.WaitAsync(ct);
                return new RappiOperationResult(true, 200);
            });
        var acceptance = accepting.Processor.IngestNewOrderAsync(11, Payload(11, "accept-wins"), timeout.Token);
        await providerEntered.Task.WaitAsync(timeout.Token);
        var cancellation = Cancel(cancelling.Processor, "accept-wins", timeout.Token);
        bool cancellationWaited;
        try
        {
            await cancellationLock.Attempted.Task.WaitAsync(timeout.Token);
            cancellationWaited = !cancellation.IsCompleted;
        }
        finally { releaseProvider.TrySetResult(); }
        await Task.WhenAll(acceptance, cancellation);
        Assert.True(cancellationWaited);
        Assert.True((await acceptance).Success);
        await using var check = fixture.Db();
        Assert.Equal(OrderStatus.Cancelled, (await check.Orders.SingleAsync()).Status);
        Assert.Equal(ExternalOrderStatus.Cancelled, (await check.ExternalDeliveryOrders.SingleAsync()).Status);
        Assert.True((await check.AppPayments.SingleAsync()).IsReversed);
        var balance = await check.InventoryBalances.SingleAsync(x => x.BranchId == 11);
        Assert.Equal(0, balance.QuantityReserved);
        Assert.Equal(5, balance.QuantityOnHand);
        accepting.Provider.Verify(x => x.AcceptOrderAsync("accept-wins", 25, It.IsAny<CancellationToken>()), Times.Once);
        cancelling.Provider.VerifyNoOtherCalls();
    }

    [PostgreSqlIntegrationFact]
    public async Task Cancellation_winning_external_order_lock_vetoes_overlapping_new_order()
    {
        await using var fixture = await RappiIsolatedBusinessDatabase.CreateAsync();
        var cancellationLock = new AdvisoryLockProbe(pauseAfterAcquired: true);
        var acceptanceLock = new AdvisoryLockProbe();
        await using var cancellingDb = fixture.Db(interceptor: cancellationLock);
        await using var acceptingDb = fixture.Db(interceptor: acceptanceLock);
        var cancelling = new Flow(cancellingDb, 1, 11);
        var accepting = new Flow(acceptingDb, 1, 11);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var cancellation = Cancel(cancelling.Processor, "cancel-wins", timeout.Token);
        await cancellationLock.Acquired.Task.WaitAsync(timeout.Token);
        var acceptance = accepting.Processor.IngestNewOrderAsync(11, Payload(11, "cancel-wins"), timeout.Token);
        bool acceptanceWaited;
        try
        {
            await acceptanceLock.Attempted.Task.WaitAsync(timeout.Token);
            acceptanceWaited = !acceptance.IsCompleted;
        }
        finally { cancellationLock.Release.TrySetResult(); }
        await Task.WhenAll(acceptance, cancellation);
        Assert.True(acceptanceWaited);
        Assert.False((await acceptance).Success);
        Assert.True((await acceptance).Held);
        await using var check = fixture.Db();
        Assert.Equal(ExternalOrderStatus.Cancelled, (await check.ExternalDeliveryOrders.SingleAsync()).Status);
        Assert.Empty(await check.Orders.ToListAsync());
        Assert.Empty(await check.AppPayments.ToListAsync());
        Assert.Empty(await check.InventoryMovements.ToListAsync());
        Assert.Empty(await check.PrintJobs.ToListAsync());
        accepting.Provider.VerifyNoOtherCalls();
        cancelling.Provider.VerifyNoOtherCalls();
    }

    private static Task Cancel(RappiOrderProcessor processor, string orderId, CancellationToken ct) =>
        (Task)typeof(RappiOrderProcessor).GetMethod("ApplyCancellationAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(processor, [11, JsonSerializer.Serialize(new { order_id = orderId, reason = "Synthetic cancellation" }), ct])!;

    private sealed class AdvisoryLockProbe(bool pauseAfterAcquired = false) : DbCommandInterceptor
    {
        public TaskCompletionSource Attempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Acquired { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int paused;

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("pg_advisory_xact_lock(1380012112", StringComparison.Ordinal))
                Attempted.TrySetResult();
            return ValueTask.FromResult(result);
        }

        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("pg_advisory_xact_lock(1380012112", StringComparison.Ordinal))
            {
                Acquired.TrySetResult();
                if (pauseAfterAcquired && Interlocked.Exchange(ref paused, 1) == 0)
                    await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    [PostgreSqlIntegrationFact]
    public async Task Isolated_http_webhook_persists_once_then_new_scope_processes_real_business_flow()
    {
        await using var fixture = await RappiIsolatedBusinessDatabase.CreateAsync();
        const string secret = "synthetic-http-only";
        Guid publicId;
        await using (var seed = fixture.Db())
        {
            var connection = await seed.DeliveryAppConnections.SingleAsync(x => x.Id == 11);
            publicId = connection.PublicId;
            connection.WebhookSubscriptions.Add(new DeliveryAppWebhookSubscription
                { EventType = "NEW_ORDER", IsActive = true, EncryptedSecret = "synthetic-envelope" });
            await seed.SaveChangesAsync();
        }
        var provider = new Mock<IRappiDeliveryProvider>(MockBehavior.Strict);
        provider.Setup(x => x.AcceptOrderAsync("http-growth", 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RappiOperationResult(true, 200));
        var protector = new Mock<IIntegrationSecretProtector>(MockBehavior.Strict);
        protector.Setup(x => x.Unprotect("synthetic-envelope")).Returns(secret);
        var signal = new Mock<IBackgroundWorkSignal<RappiWork>>();

        // Deliberately NOT WebApplicationFactory<Program>: no application startup,
        // configuration/secrets, real HttpClients, DbContext factories or hosted workers.
        // Only the real webhook controller and its explicitly isolated dependencies run.
        using var server = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddControllers().AddApplicationPart(typeof(RappiIntegrationsController).Assembly);
                services.AddAuthentication("Synthetic")
                    .AddScheme<AuthenticationSchemeOptions, SyntheticAuthenticationHandler>("Synthetic", _ => { });
                services.AddAuthorization();
                services.AddRateLimiter(options => options.AddConcurrencyLimiter("rappi-webhook", limiter => limiter.PermitLimit = 10));
                services.AddScoped<TestTenantContext>();
                services.AddScoped<ApplicationDbContext>(sp => fixture.Db(sp.GetRequiredService<TestTenantContext>()));
                services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
                services.AddScoped<ITenantExecutionContext>(sp => sp.GetRequiredService<TestTenantContext>());
                services.AddHttpContextAccessor();
                services.AddScoped<ICurrentUser, CurrentUserService>();
                services.AddSingleton<IClock>(new FakeClock(new DateTime(2026, 10, 21, 17, 0, 0, DateTimeKind.Utc)));
                services.AddSingleton(provider.Object);
                services.AddSingleton(protector.Object);
                services.AddSingleton(signal.Object);
                services.Configure<ApiPublicOptions>(_ => { });
                services.AddScoped(sp => new Flow(sp.GetRequiredService<ApplicationDbContext>(), 1, 11, provider,
                    sp.GetRequiredService<IServiceScopeFactory>()));
                services.AddScoped<IRappiOrderProcessor>(sp => sp.GetRequiredService<Flow>().Processor);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseRateLimiter();
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            }));
        using var client = server.CreateClient();
        Assert.Null(server.Services.GetService<IDbContextFactory<ApplicationDbContext>>());
        using (var anonymousAdmin = await client.GetAsync("/api/branches/11/integrations/apps"))
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousAdmin.StatusCode);
        using (var foreignBranchRequest = new HttpRequestMessage(HttpMethod.Get, "/api/branches/11/integrations/apps"))
        {
            foreignBranchRequest.Headers.Add("X-Synthetic-Actor", "branch-12-admin");
            using var forbidden = await client.SendAsync(foreignBranchRequest);
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }
        var payload = Payload(11, "http-growth");
        const string timestamp = "1792602000000";
        var signature = $"t={timestamp},sign={Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{payload}"))).ToLowerInvariant()}";
        async Task<HttpStatusCode> Send(string raw)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/integrations/rappi/webhooks/{publicId}/NEW_ORDER")
                { Content = new StringContent(raw, Encoding.UTF8, "application/json") };
            request.Headers.Add("Rappi-Signature", signature);
            using var response = await client.SendAsync(request);
            return response.StatusCode;
        }
        Assert.Equal(HttpStatusCode.Unauthorized, await Send(payload + " "));
        Assert.Equal(HttpStatusCode.OK, await Send(payload));
        Assert.Equal(HttpStatusCode.OK, await Send(payload));
        await using (var received = fixture.Db())
        {
            Assert.Equal("received", (await received.IntegrationWebhookEvents.SingleAsync()).Status);
            Assert.Empty(await received.Orders.ToListAsync());
            Assert.Empty(await received.AppPayments.ToListAsync());
        }
        signal.Verify(x => x.Pulse(), Times.Once);
        provider.VerifyNoOtherCalls();
        await using (var scope = server.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IRappiOrderProcessor>().ProcessPendingWebhookEventsAsync(default);
        await using (var scope = server.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IRappiOrderProcessor>().ProcessPendingWebhookEventsAsync(default);
        await using var persisted = fixture.Db();
        Assert.Equal("processed", (await persisted.IntegrationWebhookEvents.SingleAsync()).Status);
        Assert.Equal(OrderStatus.Taken, (await persisted.Orders.SingleAsync()).Status);
        Assert.Equal(16000, (await persisted.AppPayments.SingleAsync()).Amount);
        Assert.Equal(1, (await persisted.InventoryBalances.SingleAsync(x => x.BranchId == 11)).QuantityReserved);
        Assert.Single(await persisted.PrintJobs.ToListAsync());
        provider.Verify(x => x.AcceptOrderAsync("http-growth", 25, It.IsAny<CancellationToken>()), Times.Once);
        provider.VerifyNoOtherCalls();
    }

    // Tests HTTP authorization/branch checks, not production JWT validation.
    private sealed class SyntheticAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Request.Headers["X-Synthetic-Actor"] != "branch-12-admin")
                return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, "12"), new Claim(ClaimTypes.Role, "Admin"),
                new Claim("branch_id", "12"), new Claim("tenant_id", "1")], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    [PostgreSqlIntegrationFact]
    public async Task Accepted_growth_order_and_duplicate_have_one_payment_reservation_and_creation_print()
    {
        await using var fixture = await RappiIsolatedBusinessDatabase.CreateAsync();
        await using var db = fixture.Db();
        var flow = new Flow(db, 1, 11);
        flow.Accept("growth-duplicate");
        var first = await flow.Processor.IngestNewOrderAsync(11, Payload(11, "growth-duplicate"), default);
        var duplicate = await flow.Processor.IngestNewOrderAsync(11, Payload(11, "growth-duplicate"), default);
        Assert.True(first.Success, first.Error);
        Assert.True(duplicate.Success, duplicate.Error);
        Assert.Equal(first.InternalOrderId, duplicate.InternalOrderId);
        db.ChangeTracker.Clear();
        var order = await db.Orders.Include(x => x.OrderDetails).SingleAsync();
        var external = await db.ExternalDeliveryOrders.SingleAsync();
        var payment = await db.AppPayments.SingleAsync();
        Assert.Equal(OrderStatus.Taken, order.Status);
        Assert.Equal(11, order.BranchId);
        Assert.Equal(16000, order.Total);
        Assert.Equal(18000, order.Subtotal);
        Assert.Equal(1000, order.DiscountTotal);
        Assert.Equal(2000, external.TotalDiscountByRappi);
        Assert.Equal(1000, external.TotalDiscountByPartner);
        Assert.Equal(3000, external.TotalDiscounts);
        Assert.Equal(1000, external.TotalCharges);
        Assert.Equal(16000, payment.Amount);
        Assert.Equal(4000m, payment.EstimatedCommissionAmount);
        Assert.Equal(12000m, payment.ExpectedNetAmount);
        Assert.False(payment.IsSetted);
        Assert.False(payment.IsReversed);
        Assert.Empty(await db.BankPayments.ToListAsync());
        var balance = await db.InventoryBalances.SingleAsync(x => x.BranchId == 11);
        Assert.Equal(5, balance.QuantityOnHand);
        Assert.Equal(1, balance.QuantityReserved);
        Assert.Single(await db.InventoryMovements.Where(x => x.OrderId == order.Id).ToListAsync());
        Assert.Single(await db.OrderInventoryAllocations.Where(x => x.OrderId == order.Id).ToListAsync());
        var print = await db.PrintJobs.SingleAsync();
        Assert.Equal(11, print.BranchId);
        Assert.Equal(PrintJobStatus.Pending, print.Status);
        Assert.Contains("Synthetic strict drink", print.PayloadJson);
        flow.Provider.Verify(x => x.AcceptOrderAsync("growth-duplicate", 25, It.IsAny<CancellationToken>()), Times.Once);
        flow.Notifications.Verify(x => x.NotifyNewOrderToKitchen(It.Is<OrderDto>(x => x.Id == order.Id && x.BranchId == 11)), Times.Once);
        flow.PrintTransport.Verify(x => x.NotifyJobsAvailableAsync(11, print.Id, PrintJobKind.Kitchen, It.IsAny<CancellationToken>()), Times.Once);
        flow.Provider.VerifyNoOtherCalls();
    }

    [PostgreSqlIntegrationFact]
    public async Task Marking_ready_consumes_reserved_stock_prints_once_and_sends_ready_once()
    {
        await using var fixture = await RappiIsolatedBusinessDatabase.CreateAsync();
        await using var db = fixture.Db();
        var flow = new Flow(db, 1, 12);
        flow.Accept("ready-flow");
        flow.Provider.Setup(x => x.ReadyForPickupAsync("ready-flow", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RappiOperationResult(true, 200));
        var accepted = await flow.Processor.IngestNewOrderAsync(12, Payload(12, "ready-flow"), default);
        Assert.True(accepted.Success, accepted.Error);
        Assert.Empty(await db.PrintJobs.ToListAsync());
        db.ChangeTracker.Clear();
        await flow.MarkReady(accepted.InternalOrderId!.Value);
        db.ChangeTracker.Clear();
        await flow.MarkReady(accepted.InternalOrderId.Value);
        await flow.SendReadyOutbox();
        await flow.SendReadyOutbox();
        db.ChangeTracker.Clear();
        Assert.Equal(OrderStatus.Ready, (await db.Orders.SingleAsync()).Status);
        var balance = await db.InventoryBalances.SingleAsync(x => x.BranchId == 12);
        Assert.Equal(4, balance.QuantityOnHand);
        Assert.Equal(0, balance.QuantityReserved);
        Assert.Equal(1, (await db.OrderInventoryAllocations.SingleAsync()).ConsumedQuantity);
        Assert.Single(await db.InventoryMovements.Where(x => x.Type == InventoryMovementType.StrictConsumption).ToListAsync());
        Assert.Single(await db.PrintJobs.ToListAsync());
        var outbox = await db.IntegrationWebhookEvents.SingleAsync();
        Assert.Equal("processed", outbox.Status);
        Assert.Equal(1, outbox.AttemptCount);
        flow.Provider.Verify(x => x.AcceptOrderAsync("ready-flow", 25, It.IsAny<CancellationToken>()), Times.Once);
        flow.Provider.Verify(x => x.ReadyForPickupAsync("ready-flow", It.IsAny<CancellationToken>()), Times.Once);
        flow.Provider.VerifyNoOtherCalls();
        flow.PrintTransport.Verify(x => x.NotifyJobsAvailableAsync(12, It.IsAny<long>(), PrintJobKind.Kitchen,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [PostgreSqlIntegrationFact]
    public async Task Failed_provider_acceptance_rolls_back_real_order_payment_inventory_and_print()
    {
        await using var fixture = await RappiIsolatedBusinessDatabase.CreateAsync();
        await using var db = fixture.Db();
        var flow = new Flow(db, 1, 11);
        flow.Provider.Setup(x => x.AcceptOrderAsync("accept-failed", 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RappiOperationResult(false, 503, "Synthetic unavailable"));
        flow.Provider.Setup(x => x.GetOrderEventsAsync("accept-failed", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RappiOrderEventsResult(false, Error: "Synthetic unavailable"));
        var result = await flow.Processor.IngestNewOrderAsync(11, Payload(11, "accept-failed"), default);
        Assert.False(result.Success);
        db.ChangeTracker.Clear();
        Assert.Empty(await db.Orders.ToListAsync());
        Assert.Empty(await db.AppPayments.ToListAsync());
        Assert.Empty(await db.PrintJobs.ToListAsync());
        Assert.Empty(await db.InventoryMovements.ToListAsync());
        Assert.Empty(await db.OrderInventoryAllocations.ToListAsync());
        Assert.Equal(0, (await db.InventoryBalances.SingleAsync(x => x.BranchId == 11)).QuantityReserved);
        Assert.Equal(ExternalOrderStatus.SyncError, (await db.ExternalDeliveryOrders.SingleAsync()).Status);
        flow.Provider.Verify(x => x.AcceptOrderAsync("accept-failed", 25, It.IsAny<CancellationToken>()), Times.Once);
        flow.Provider.Verify(x => x.GetOrderEventsAsync("accept-failed", It.IsAny<CancellationToken>()), Times.Once);
        flow.Provider.VerifyNoOtherCalls();
        flow.Notifications.VerifyNoOtherCalls();
        flow.PrintTransport.VerifyNoOtherCalls();
    }

    [PostgreSqlIntegrationFact]
    public async Task Synthetic_connections_map_each_branch_and_tenant_filters_hide_foreign_orders()
    {
        await using var fixture = await RappiIsolatedBusinessDatabase.CreateAsync();
        foreach (var branch in new[] { 11, 12 })
        {
            await using var db = fixture.Db();
            var flow = new Flow(db, 1, branch);
            flow.Accept("shared-provider-id");
            var result = await flow.Processor.IngestNewOrderAsync(branch, Payload(branch, "shared-provider-id"), default);
            Assert.True(result.Success, result.Error);
            var order = await db.Orders.SingleAsync(x => x.Id == result.InternalOrderId);
            Assert.Equal(branch, order.BranchId);
            Assert.Equal(branch, (await db.AppPayments.SingleAsync(x => x.OrderId == order.Id)).AppId);
        }
        await using var other = fixture.Db(2);
        Assert.Empty(await other.Orders.ToListAsync());
        Assert.Empty(await other.AppPayments.ToListAsync());
        Assert.Empty(await other.PrintJobs.ToListAsync());
        Assert.Empty(await other.InventoryMovements.ToListAsync());
        var otherFlow = new Flow(other, 2, 21);
        var foreign = await otherFlow.Processor.IngestNewOrderAsync(11, Payload(11, "foreign-attempt"), default);
        Assert.False(foreign.Success);
        otherFlow.Provider.VerifyNoOtherCalls();
        otherFlow.Accept("shared-provider-id");
        Assert.True((await otherFlow.Processor.IngestNewOrderAsync(21, Payload(21, "shared-provider-id"), default)).Success);
        Assert.Equal(21, (await other.Orders.SingleAsync()).BranchId);
        Assert.Equal(2, (await other.Orders.SingleAsync()).TenantId);
        await using var first = fixture.Db();
        Assert.Equal(2, await first.Orders.CountAsync());
    }

    [PostgreSqlIntegrationFact]
    public async Task Store_from_another_branch_is_held_without_reservation_or_provider_call()
    {
        await using var fixture = await RappiIsolatedBusinessDatabase.CreateAsync();
        await using var db = fixture.Db();
        var flow = new Flow(db, 1, 11);
        var result = await flow.Processor.IngestNewOrderAsync(11, Payload(12, "wrong-store"), default);
        Assert.False(result.Success);
        Assert.True(result.Held);
        Assert.Empty(await db.Orders.ToListAsync());
        Assert.Empty(await db.AppPayments.ToListAsync());
        Assert.Empty(await db.InventoryMovements.ToListAsync());
        flow.Provider.VerifyNoOtherCalls();
    }

    internal static string Payload(int branch, string orderId) => JsonSerializer.Serialize(new
    {
        store = new { internal_id = $"isolated-store-{branch}", external_id = $"isolated-store-{branch}" },
        customer = new { first_name = "Synthetic", last_name = "Only" },
        order_detail = new
        {
            order_id = orderId, delivery_method = "delivery", payment_method = "rappi_pay", cooking_time = 25,
            totals = new { total_order = 16000, total_products = 18000, total_discounts = 3000, total_discount_by_partner = 1000, charges = new { shipping = 1000 } },
            items = new[] { new { id = $"isolated-product-{branch}", sku = $"product-{branch}", name = "Synthetic strict drink", type = "PRODUCT", quantity = 1, price = 18000 } },
            discounts = new[] { new { title = "Synthetic Growth", type = "campaign", sku = $"product-{branch}", value = 3000, amount_by_rappi = 2000, amount_by_partner = 1000 } }
        }
    });

    private sealed class Flow
    {
        public Mock<IRappiDeliveryProvider> Provider { get; }
        public Mock<IOrderNotificationService> Notifications { get; } = new();
        public Mock<IPrintAgentNotifier> PrintTransport { get; } = new();
        public RappiOrderProcessor Processor { get; }
        private readonly ApplicationDbContext db;
        private readonly IClock clock = new FakeClock(new DateTime(2026, 10, 21, 17, 0, 0, DateTimeKind.Utc));
        private readonly ChangeOrderStatusHandler handler;

        public Flow(ApplicationDbContext db, int tenantId, int branch, Mock<IRappiDeliveryProvider>? provider = null,
            IServiceScopeFactory? scopeFactory = null)
        {
            this.db = db;
            Provider = provider ?? new(MockBehavior.Strict);
            var tenant = new TestTenantContext(tenantId);
            var actor = Mock.Of<ICurrentUser>(x => x.Id == branch && x.BranchId == branch && x.Role == "Admin" && x.IsAuthenticated);
            var inventory = new InventoryService(db, actor, tenant, clock, new TestBranchContext(branch));
            var orders = new OrderRepository(db, clock, tenant);
            var prints = new PrintQueueService(db, Options.Create(new ApiPublicOptions { BaseUrl = "http://localhost" }),
                Options.Create(new BrandingOptions()), clock, PrintTransport.Object, tenant, NullLogger<PrintQueueService>.Instance);
            var mapper = new Mock<IMapper>();
            mapper.Setup(x => x.Map<OrderDto>(It.IsAny<object>())).Returns((object input) =>
            {
                var order = (Order)input;
                return new OrderDto { Id = order.Id, BranchId = order.BranchId, Status = order.Status, Type = order.Type, Total = order.Total };
            });
            var routes = Mock.Of<IDeliveryRouteWorkflowService>();
            Processor = new RappiOrderProcessor(db, Provider.Object, clock, orders, mapper.Object, Notifications.Object,
                routes, NullLogger<RappiOrderProcessor>.Instance, prints, inventory, scopeFactory);
            handler = new ChangeOrderStatusHandler(orders, db, mapper.Object, actor, new OrderBusinessRulesService(clock),
                Notifications.Object, routes, prints, Mock.Of<ILoyaltyCycleService>(), NullLogger<ChangeOrderStatusHandler>.Instance,
                new ExternalDeliveryStatusSyncService(db, clock), inventory);
        }

        public void Accept(string id) => Provider.Setup(x => x.AcceptOrderAsync(id, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RappiOperationResult(true, 200));
        public Task<OrderDto> MarkReady(int orderId) => handler.Handle(new ChangeOrderStatusCommand
            { Id = orderId, StatusChange = new ChangeOrderStatusDto { Status = OrderStatus.Ready } }, default);
        public Task SendReadyOutbox() => (Task)typeof(RappiIntegrationWorker)
            .GetMethod("ProcessReadyOutboxAsync", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [db, Provider.Object, clock, CancellationToken.None])!;
    }
}
