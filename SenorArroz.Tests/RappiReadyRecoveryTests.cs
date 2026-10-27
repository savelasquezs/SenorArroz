using System.Reflection;
using System.Data.Common;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;
using SenorArroz.API.Controllers;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Integrations;
using SenorArroz.Shared.Models;
using Testcontainers.PostgreSql;

namespace SenorArroz.Tests;

public sealed class RappiReadyRecoveryTests : RappiReadyRecoveryDatabase
{
    [PostgreSqlIntegrationFact]
    public async Task Complete_send_has_bounded_cancellation_and_attempt_timeout_remains_retryable()
    {
        await using var db = Db();
        await RappiReadyTestData.SeedAsync(db);
        var provider = new Mock<IRappiDeliveryProvider>(MockBehavior.Strict);
        provider.Setup(x => x.ReadyForPickupAsync("ready-test-order", It.IsAny<CancellationToken>()))
            .Returns((string _, CancellationToken token) =>
            {
                Assert.True(token.CanBeCanceled); // The worker itself was called with CancellationToken.None.
                throw new OperationCanceledException("simulated response-body timeout", token);
            });
        Assert.True(RappiReadyOutboxPolicy.AttemptTimeout > TimeSpan.Zero);
        Assert.True(RappiReadyOutboxPolicy.AttemptTimeout < RappiReadyOutboxPolicy.ProcessingLease);
        await RappiReadyTestData.ProcessAsync(db, provider.Object);
        var row = await db.IntegrationWebhookEvents.SingleAsync();
        Assert.Equal(RappiReadyOutboxPolicy.Pending, row.Status);
        Assert.Equal(1, row.AttemptCount);
        Assert.DoesNotContain("response-body", row.LastError);
    }

    [PostgreSqlIntegrationFact]
    public async Task Tenth_failure_becomes_visible_and_cannot_send_again_automatically()
    {
        await using var db = Db();
        await RappiReadyTestData.SeedAsync(db, attempts: 9);
        var provider = new Mock<IRappiDeliveryProvider>(MockBehavior.Strict);
        provider.Setup(x => x.ReadyForPickupAsync("ready-test-order", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RappiOperationResult(false, 424, "sensitive-provider-body"));

        await RappiReadyTestData.ProcessAsync(db, provider.Object);
        await RappiReadyTestData.ProcessAsync(db, provider.Object);

        var row = await db.IntegrationWebhookEvents.SingleAsync();
        Assert.Equal(RappiReadyOutboxPolicy.Failed, row.Status);
        Assert.Equal(10, row.AttemptCount);
        Assert.Contains("424", row.LastError);
        Assert.DoesNotContain("sensitive-provider-body", row.LastError);
        Assert.True(RappiReadyOutboxPolicy.CanRequeue(row));
        provider.Verify(x => x.ReadyForPickupAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [PostgreSqlIntegrationFact]
    public async Task Legacy_exhausted_pending_is_failed_without_calling_provider()
    {
        await using var db = Db();
        await RappiReadyTestData.SeedAsync(db, attempts: 10);
        await RappiReadyTestData.ProcessAsync(db, Mock.Of<IRappiDeliveryProvider>(MockBehavior.Strict));
        Assert.Equal(RappiReadyOutboxPolicy.Failed, (await db.IntegrationWebhookEvents.SingleAsync()).Status);
    }

    [RappiPostgreSqlIntegrationTheory]
    [InlineData(OrderStatus.Cancelled, true, true, RappiReadyOutboxPolicy.Skipped)]
    [InlineData(OrderStatus.Delivered, true, true, RappiReadyOutboxPolicy.Skipped)]
    [InlineData(OrderStatus.InPreparation, true, true, RappiReadyOutboxPolicy.Failed)]
    [InlineData(OrderStatus.Ready, false, true, RappiReadyOutboxPolicy.Failed)]
    [InlineData(OrderStatus.Ready, true, false, RappiReadyOutboxPolicy.Failed)]
    public async Task Ineligible_order_is_not_sent(OrderStatus status, bool active, bool enabled, string expectedStatus)
    {
        await using var db = Db();
        await RappiReadyTestData.SeedAsync(db, orderStatus: status, active: active, enabled: enabled);
        await RappiReadyTestData.ProcessAsync(db, Mock.Of<IRappiDeliveryProvider>(MockBehavior.Strict));
        var row = await db.IntegrationWebhookEvents.SingleAsync();
        Assert.Equal(expectedStatus, row.Status);
        Assert.Equal(0, row.AttemptCount);
    }

    [PostgreSqlIntegrationFact]
    public async Task Success_is_not_sent_again_and_does_not_create_another_outbox_event()
    {
        await using var db = Db();
        await RappiReadyTestData.SeedAsync(db);
        var provider = new Mock<IRappiDeliveryProvider>(MockBehavior.Strict);
        provider.Setup(x => x.ReadyForPickupAsync("ready-test-order", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RappiOperationResult(true, 200));
        await RappiReadyTestData.ProcessAsync(db, provider.Object);
        await RappiReadyTestData.ProcessAsync(db, provider.Object);
        var row = await db.IntegrationWebhookEvents.SingleAsync();
        Assert.Equal("processed", row.Status);
        Assert.Equal(1, row.AttemptCount);
        Assert.NotNull(row.ProcessedAt);
        provider.Verify(x => x.ReadyForPickupAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [PostgreSqlIntegrationFact]
    public async Task Invalid_payload_shape_is_failed_without_repeated_attempts_or_raw_error()
    {
        await using var db = Db();
        await RappiReadyTestData.SeedAsync(db, attempts: 3);
        var row = await db.IntegrationWebhookEvents.SingleAsync();
        row.PayloadJson = """{"private":"private-data"}""";
        await db.SaveChangesAsync();
        await RappiReadyTestData.ProcessAsync(db, Mock.Of<IRappiDeliveryProvider>(MockBehavior.Strict));
        row = await db.IntegrationWebhookEvents.SingleAsync();
        Assert.Equal(RappiReadyOutboxPolicy.Failed, row.Status);
        Assert.Equal(3, row.AttemptCount);
        Assert.DoesNotContain("private-data", row.LastError);
    }

    [PostgreSqlIntegrationFact]
    public async Task Retry_rejects_wrong_branch_and_does_not_change_failed_event()
    {
        await using var db = Db();
        await RappiReadyTestData.SeedAsync(db, eventStatus: RappiReadyOutboxPolicy.Failed, attempts: 10);
        var response = await RappiReadyTestData.Controller(db, branchId: 2).RetryReadyForPickup(1, default);
        Assert.IsType<ForbidResult>(response.Result);
        Assert.Equal(10, (await db.IntegrationWebhookEvents.SingleAsync()).AttemptCount);
    }

    [PostgreSqlIntegrationFact]
    public async Task Pending_or_processed_retry_is_idempotent_and_does_not_reset_attempt_count()
    {
        await using var db = Db();
        await RappiReadyTestData.SeedAsync(db, attempts: 4);
        var controller = RappiReadyTestData.Controller(db);
        Assert.IsType<OkObjectResult>((await controller.RetryReadyForPickup(1, default)).Result);
        var row = await db.IntegrationWebhookEvents.SingleAsync();
        Assert.Equal(4, row.AttemptCount);
        row.Status = "processed";
        await db.SaveChangesAsync();
        Assert.IsType<OkObjectResult>((await controller.RetryReadyForPickup(1, default)).Result);
        Assert.Equal("processed", row.Status);
        Assert.Equal(4, row.AttemptCount);
    }

    [PostgreSqlIntegrationFact]
    public async Task An_in_flight_event_cannot_be_requeued()
    {
        await using var db = Db();
        await RappiReadyTestData.SeedAsync(db, eventStatus: "outbox_processing", attempts: 4);
        var response = await RappiReadyTestData.Controller(db).RetryReadyForPickup(1, default);
        Assert.IsType<ConflictObjectResult>(response.Result);
        var row = await db.IntegrationWebhookEvents.SingleAsync();
        Assert.Equal("outbox_processing", row.Status);
        Assert.Equal(4, row.AttemptCount);
    }

    [RappiPostgreSqlIntegrationTheory]
    [InlineData(OrderStatus.Cancelled, true, true)]
    [InlineData(OrderStatus.Delivered, true, true)]
    [InlineData(OrderStatus.InPreparation, true, true)]
    [InlineData(OrderStatus.Ready, false, true)]
    [InlineData(OrderStatus.Ready, true, false)]
    public async Task Retry_rejects_ineligible_order_without_resetting_failures(OrderStatus status, bool active, bool enabled)
    {
        await using var db = Db();
        await RappiReadyTestData.SeedAsync(db, eventStatus: RappiReadyOutboxPolicy.Failed, attempts: 10,
            orderStatus: status, active: active, enabled: enabled);
        var response = await RappiReadyTestData.Controller(db).RetryReadyForPickup(1, default);
        Assert.IsType<BadRequestObjectResult>(response.Result);
        Assert.Equal(10, (await db.IntegrationWebhookEvents.SingleAsync()).AttemptCount);
    }

    [PostgreSqlIntegrationFact]
    public async Task Order_diagnostics_expose_failure_and_retryability_without_raw_provider_content()
    {
        await using var db = Db();
        await RappiReadyTestData.SeedAsync(db, eventStatus: RappiReadyOutboxPolicy.Failed, attempts: 10);
        var row = await db.IntegrationWebhookEvents.SingleAsync();
        row.LastError = "private-old-provider-body";
        await db.SaveChangesAsync();
        var response = await RappiReadyTestData.Controller(db).GetOrders(1, default);
        var result = Assert.IsType<OkObjectResult>(response.Result);
        var json = JsonSerializer.Serialize(result.Value);
        Assert.Contains("outbox_failed", json);
        Assert.Contains("retryable", json);
        Assert.DoesNotContain("private-old-provider-body", json);
    }

    [PostgreSqlIntegrationFact]
    public async Task Operational_diagnostics_report_legacy_processing_without_replaying_or_exposing_payload()
    {
        await using var db = Db();
        await RappiReadyTestData.SeedAsync(db);
        db.IntegrationWebhookEvents.AddRange(
            new IntegrationWebhookEvent { Id = 2, ConnectionId = 1, Provider = "rappi", EventKey = "legacy-claim",
                EventType = "NEW_ORDER", Status = "processing", PayloadJson = "{\"private\":\"local-only\"}" },
            new IntegrationWebhookEvent { Id = 3, ConnectionId = 1, Provider = "rappi", EventKey = "leased-claim",
                EventType = "NEW_ORDER", Status = "processing_leased", PayloadJson = "{}" });
        await db.SaveChangesAsync();

        var result = Assert.IsType<OkObjectResult>(
            (await RappiReadyTestData.Controller(db).OperationalStatus(1, default)).Result);
        var json = JsonSerializer.Serialize(result.Value);
        Assert.Contains("\"webhookLegacyProcessing\":1", json);
        Assert.DoesNotContain("local-only", json);
        Assert.Equal("processing", (await db.IntegrationWebhookEvents.SingleAsync(x => x.EventKey == "legacy-claim")).Status);
    }

    [PostgreSqlIntegrationFact]
    public void Retry_endpoint_requires_authenticated_operational_role()
    {
        var authorize = typeof(RappiIntegrationsController).GetMethod("RetryReadyForPickup")!
            .GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Equal("Superadmin, Admin, Cashier", authorize.Roles);
    }
}

/// <summary>Owns a disposable local database; never initializes a supplied database.</summary>
public abstract class RappiReadyRecoveryDatabase : IAsyncLifetime
{
    private PostgreSqlContainer? postgres;
    private string? serverConnection;
    private string? databaseName;
    private DbContextOptions<ApplicationDbContext> options = null!;

    public async Task InitializeAsync()
    {
        var supplied = Environment.GetEnvironmentVariable("RAPPI_POSTGRES_TEST_CONNECTION");
        string connection;
        if (!string.IsNullOrWhiteSpace(supplied))
        {
            var builder = new NpgsqlConnectionStringBuilder(supplied);
            if (builder.Host is not ("localhost" or "127.0.0.1" or "::1"))
                throw new InvalidOperationException("Rappi recovery tests accept only a loopback PostgreSQL server.");
            builder.Database = "postgres";
            builder.Pooling = false;
            serverConnection = builder.ConnectionString;
            databaseName = $"rappi_ready_test_{Guid.NewGuid():N}";
            await using var server = new NpgsqlConnection(serverConnection);
            await server.OpenAsync();
            await new NpgsqlCommand($"CREATE DATABASE {databaseName}", server).ExecuteNonQueryAsync();
            builder.Database = databaseName;
            connection = builder.ConnectionString;
        }
        else
        {
            postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
            await postgres.StartAsync();
            connection = postgres.GetConnectionString();
        }
        options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).Options;
        await using var db = Db();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (serverConnection is not null && databaseName is not null)
        {
            await using var server = new NpgsqlConnection(serverConnection);
            await server.OpenAsync();
            await new NpgsqlCommand($"DROP DATABASE IF EXISTS {databaseName} WITH (FORCE)", server).ExecuteNonQueryAsync();
        }
        if (postgres is not null) await postgres.DisposeAsync();
    }

    protected ApplicationDbContext Db(int tenantId = 1, IInterceptor? interceptor = null)
    {
        var tenant = new TestTenantContext(tenantId);
        var configured = interceptor is null ? options
            : new DbContextOptionsBuilder<ApplicationDbContext>(options).AddInterceptors(interceptor).Options;
        return new(configured, currentTenant: tenant, tenantExecutionContext: tenant);
    }

}

public sealed class RappiReadyRecoveryPostgreSqlTests : RappiReadyRecoveryDatabase
{
    [PostgreSqlIntegrationFact]
    public async Task Manual_webhook_retry_keeps_attempt_fencing_monotonic_and_rejects_foreign_branch()
    {
        await using var db = Db();
        await RappiReadyTestData.SeedAsync(db);
        db.IntegrationWebhookEvents.Add(new IntegrationWebhookEvent
        {
            Id = 2, ConnectionId = 1, Provider = "rappi", EventKey = "manual-cancel", EventType = "ORDER_EVENT_CANCEL",
            Status = "failed", AttemptCount = 8, PayloadJson = "{}", LastError = "private-provider-response",
            CreatedAt = RappiReadyTestData.Now, UpdatedAt = RappiReadyTestData.Now
        });
        await db.SaveChangesAsync();
        Assert.IsType<ForbidResult>((await RappiReadyTestData.Controller(db, branchId: 2).RetryWebhook(2, default)).Result);
        Assert.IsType<OkObjectResult>((await RappiReadyTestData.Controller(db).RetryWebhook(2, default)).Result);
        var row = await db.IntegrationWebhookEvents.AsNoTracking().SingleAsync(x => x.Id == 2);
        Assert.Equal("manual_retry_received", row.Status);
        Assert.Equal(8, row.AttemptCount);
        Assert.Null(row.LastError);
        Assert.IsType<BadRequestObjectResult>((await RappiReadyTestData.Controller(db).RetryWebhook(2, default)).Result);
    }

    [PostgreSqlIntegrationFact]
    public async Task Diagnostics_are_paged_scoped_and_do_not_return_raw_payload_or_provider_errors()
    {
        await using var db = Db();
        await RappiReadyTestData.SeedAsync(db);
        db.IntegrationWebhookEvents.Add(new IntegrationWebhookEvent
        {
            Id = 2, ConnectionId = 1, Provider = "rappi", EventKey = "failed-menu", EventType = "MENU_REJECTED",
            Status = "failed", AttemptCount = 5, PayloadJson = "{\"private\":\"secret-test-payload\"}",
            LastError = "secret-provider-response", CreatedAt = RappiReadyTestData.Now, UpdatedAt = RappiReadyTestData.Now
        });
        await db.SaveChangesAsync();
        var result = Assert.IsType<OkObjectResult>((await RappiReadyTestData.Controller(db).GetEvents(1, default, 1, 1)).Result);
        var response = Assert.IsType<ApiResponse<object>>(result.Value);
        var page = Assert.IsType<PagedResult<object>>(response.Data);
        Assert.Equal(1, page.TotalCount);
        Assert.Single(page.Items);
        var json = JsonSerializer.Serialize(response);
        Assert.DoesNotContain("secret-test-payload", json);
        Assert.DoesNotContain("secret-provider-response", json);
        Assert.IsType<BadRequestObjectResult>((await RappiReadyTestData.Controller(db).RetryWebhook(2, default)).Result);
    }

    [PostgreSqlIntegrationFact]
    public async Task Shutdown_cancels_full_send_and_retains_active_lease_without_resend()
    {
        await using var first = Db();
        await RappiReadyTestData.SeedAsync(first);
        using var shutdown = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new Mock<IRappiDeliveryProvider>(MockBehavior.Strict);
        provider.Setup(x => x.ReadyForPickupAsync("ready-test-order", It.IsAny<CancellationToken>()))
            .Returns(async (string _, CancellationToken token) =>
            {
                Assert.True(token.CanBeCanceled);
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new RappiOperationResult(true, 200);
            });
        var processing = RappiReadyTestData.ProcessAsync(first, provider.Object, cancellationToken: shutdown.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        shutdown.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);
        await using var second = Db();
        await RappiReadyTestData.ProcessAsync(second, provider.Object);
        var row = await second.IntegrationWebhookEvents.SingleAsync();
        Assert.Equal(RappiReadyOutboxPolicy.Processing, row.Status);
        Assert.Equal(1, row.AttemptCount);
        provider.Verify(x => x.ReadyForPickupAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [PostgreSqlIntegrationFact]
    public async Task Completion_fence_survives_sub_microsecond_clock_precision()
    {
        await using var db = Db();
        await RappiReadyTestData.SeedAsync(db);
        var provider = new Mock<IRappiDeliveryProvider>(MockBehavior.Strict);
        provider.Setup(x => x.ReadyForPickupAsync("ready-test-order", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RappiOperationResult(true, 200));
        var highPrecisionNow = new DateTime(2026, 10, 21, 15, 0, 0, DateTimeKind.Utc).AddTicks(1234567);
        await RappiReadyTestData.ProcessAsync(db, provider.Object, highPrecisionNow);
        var row = await db.IntegrationWebhookEvents.SingleAsync();
        Assert.Equal("processed", row.Status);
        Assert.Equal(1, row.AttemptCount);
    }

    [PostgreSqlIntegrationFact]
    public async Task Legacy_terminalization_cannot_overwrite_concurrent_manual_requeue()
    {
        await using var setup = Db();
        await RappiReadyTestData.SeedAsync(setup, attempts: 10);
        var gate = new BlockFailureTransition();
        await using var workerDb = Db(interceptor: gate);
        await using var retryDb = Db();
        var processing = RappiReadyTestData.ProcessAsync(workerDb, Mock.Of<IRappiDeliveryProvider>(MockBehavior.Strict));
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            Assert.IsType<OkObjectResult>((await RappiReadyTestData.Controller(retryDb).RetryReadyForPickup(1, default)).Result);
        }
        finally { gate.Release.TrySetResult(); }
        await processing;
        await using var check = Db();
        var row = await check.IntegrationWebhookEvents.SingleAsync();
        Assert.Equal(RappiReadyOutboxPolicy.Pending, row.Status);
        Assert.Equal(0, row.AttemptCount);
    }

    [PostgreSqlIntegrationFact]
    public async Task Two_workers_and_manual_retry_do_not_duplicate_an_active_send()
    {
        await using var setup = Db();
        await RappiReadyTestData.SeedAsync(setup);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new Mock<IRappiDeliveryProvider>(MockBehavior.Strict);
        provider.Setup(x => x.ReadyForPickupAsync("ready-test-order", It.IsAny<CancellationToken>()))
            .Returns(async () => { entered.TrySetResult(); await release.Task; return new RappiOperationResult(true, 200); });
        await using var first = Db();
        await using var second = Db();
        await using var retry = Db();
        var processing = RappiReadyTestData.ProcessAsync(first, provider.Object);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await RappiReadyTestData.ProcessAsync(second, provider.Object);
            Assert.IsType<ConflictObjectResult>((await RappiReadyTestData.Controller(retry).RetryReadyForPickup(1, default)).Result);
        }
        finally { release.TrySetResult(); }
        await processing;
        provider.Verify(x => x.ReadyForPickupAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [RappiPostgreSqlIntegrationTheory]
    [InlineData(4, 16, "processed", 5)]
    [InlineData(4, 1, RappiReadyOutboxPolicy.Processing, 4)]
    [InlineData(10, 16, RappiReadyOutboxPolicy.Failed, 10)]
    public async Task Only_abandoned_claims_with_attempts_remaining_are_recovered(int attempts, int ageMinutes, string expectedStatus, int expectedAttempts)
    {
        await using var db = Db();
        await RappiReadyTestData.SeedAsync(db, attempts: attempts, eventStatus: RappiReadyOutboxPolicy.Processing);
        var row = await db.IntegrationWebhookEvents.SingleAsync();
        row.UpdatedAt = new DateTime(2026, 10, 21, 15, 0, 0, DateTimeKind.Utc).AddMinutes(-ageMinutes);
        await db.SaveChangesAsync();
        var provider = new Mock<IRappiDeliveryProvider>(MockBehavior.Strict);
        if (expectedStatus == "processed")
            provider.Setup(x => x.ReadyForPickupAsync("ready-test-order", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RappiOperationResult(true, 200));
        await RappiReadyTestData.ProcessAsync(db, provider.Object);
        row = await db.IntegrationWebhookEvents.SingleAsync();
        Assert.Equal(expectedStatus, row.Status);
        Assert.Equal(expectedAttempts, row.AttemptCount);
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Concurrent_requeue_reuses_one_event_and_pulses_once()
    {
        await using var firstDb = Db();
        await RappiReadyTestData.SeedAsync(firstDb, eventStatus: RappiReadyOutboxPolicy.Failed, attempts: 10);
        await using var secondDb = Db();
        var signal = new Mock<IBackgroundWorkSignal<RappiWork>>();
        var responses = await Task.WhenAll(
            RappiReadyTestData.Controller(firstDb, signal: signal.Object).RetryReadyForPickup(1, default),
            RappiReadyTestData.Controller(secondDb, signal: signal.Object).RetryReadyForPickup(1, default));
        Assert.All(responses, x => Assert.IsType<OkObjectResult>(x.Result));
        await using var check = Db();
        var row = await check.IntegrationWebhookEvents.SingleAsync();
        Assert.Equal(RappiReadyOutboxPolicy.Pending, row.Status);
        Assert.Equal(0, row.AttemptCount);
        Assert.Null(row.LastError);
        signal.Verify(x => x.Pulse(), Times.Once);
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Legacy_exhausted_pending_can_be_requeued_then_processed_once()
    {
        await using var db = Db();
        await RappiReadyTestData.SeedAsync(db, eventStatus: RappiReadyOutboxPolicy.Failed, attempts: 10);
        var row = await db.IntegrationWebhookEvents.SingleAsync();
        row.Status = RappiReadyOutboxPolicy.Pending;
        await db.SaveChangesAsync();
        Assert.IsType<OkObjectResult>((await RappiReadyTestData.Controller(db).RetryReadyForPickup(1, default)).Result);
        db.ChangeTracker.Clear();
        var provider = new Mock<IRappiDeliveryProvider>(MockBehavior.Strict);
        provider.Setup(x => x.ReadyForPickupAsync("ready-test-order", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RappiOperationResult(true, 200));
        await RappiReadyTestData.ProcessAsync(db, provider.Object);
        await RappiReadyTestData.ProcessAsync(db, provider.Object);
        Assert.Equal("processed", (await db.IntegrationWebhookEvents.SingleAsync()).Status);
        provider.Verify(x => x.ReadyForPickupAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Different_tenant_cannot_read_or_requeue_the_event()
    {
        await using var setup = Db();
        await RappiReadyTestData.SeedAsync(setup, eventStatus: RappiReadyOutboxPolicy.Failed, attempts: 10);
        await using var other = Db(2);
        Assert.IsType<NotFoundResult>((await RappiReadyTestData.Controller(other).RetryReadyForPickup(1, default)).Result);
        Assert.Empty(await other.IntegrationWebhookEvents.ToListAsync());
        await using var check = Db();
        Assert.Equal(RappiReadyOutboxPolicy.Failed, (await check.IntegrationWebhookEvents.SingleAsync()).Status);
    }

    private sealed class BlockFailureTransition : DbCommandInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("integration_webhook_event", StringComparison.Ordinal)
                && (command.CommandText.Contains(RappiReadyOutboxPolicy.Failed, StringComparison.Ordinal)
                    || command.Parameters.Cast<DbParameter>().Any(x => Equals(x.Value, RappiReadyOutboxPolicy.Failed))))
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return result;
        }
    }
}

internal static class RappiReadyTestData
{
    internal static readonly DateTime Now = new(2026, 10, 21, 15, 0, 0, DateTimeKind.Utc);
    public static async Task SeedAsync(ApplicationDbContext db, int attempts = 0,
        string eventStatus = RappiReadyOutboxPolicy.Pending, OrderStatus orderStatus = OrderStatus.Ready,
        bool active = true, bool enabled = true)
    {
        var tenant = new Tenant { Id = 1, Name = "Local Rappi test", Slug = "local-rappi-test" };
        var branch = new Branch { Id = 1, Tenant = tenant, Name = "Local test", Address = "Test", Phone1 = "0" };
        var user = new User { Id = 1, Branch = branch, Name = "Test", Email = "rappi@example.test", PasswordHash = "not-a-password", Role = UserRole.Cashier };
        var bank = new Bank { Id = 1, Branch = branch, Name = "Test bank" };
        var app = new App { Id = 1, Bank = bank, Name = "Test app" };
        var connection = new DeliveryAppConnection { Id = 1, Branch = branch, Provider = "rappi", FinancialApp = app,
            DisplayName = "Local Rappi", TechnicalUser = user, IsActive = active, IsVerified = true };
        var store = new DeliveryAppStore { Id = 1, Connection = connection, RappiStoreId = "local-test-store",
            Name = "Test store", ManualReadyForPickupEnabled = enabled, IsParent = true };
        var order = new Order { Id = 1, Branch = branch, TakenBy = user, Type = OrderType.Delivery,
            Status = orderStatus, StatusTimes = "{}", ExternalFulfillmentProvider = "rappi", ExternalOrderId = "ready-test-order" };
        db.ExternalDeliveryOrders.Add(new ExternalDeliveryOrder { Id = 1, Connection = connection, Store = store,
            BranchId = 1, ExternalOrderId = "ready-test-order", InternalOrder = order,
            Status = ExternalOrderStatus.Accepted, CreatedAt = Now, UpdatedAt = Now });
        db.IntegrationWebhookEvents.Add(new IntegrationWebhookEvent { Id = 1, ConnectionId = 1, Provider = "rappi",
            EventKey = RappiReadyOutboxPolicy.EventKey(1, "ready-test-order"), EventType = RappiReadyOutboxPolicy.EventType,
            PayloadJson = JsonSerializer.Serialize(new { externalOrderId = "ready-test-order" }),
            Status = eventStatus, AttemptCount = attempts, CreatedAt = Now, UpdatedAt = Now });
        await db.SaveChangesAsync();
    }

    public static RappiIntegrationsController Controller(ApplicationDbContext db, int branchId = 1,
        IBackgroundWorkSignal<RappiWork>? signal = null)
    {
        var actor = new Mock<ICurrentUser>();
        actor.SetupGet(x => x.Role).Returns("Cashier");
        actor.SetupGet(x => x.BranchId).Returns(branchId);
        actor.SetupGet(x => x.IsAuthenticated).Returns(true);
        var provider = new Mock<IRappiDeliveryProvider>(MockBehavior.Strict);
        provider.SetupGet(x => x.CredentialsConfigured).Returns(false);
        return new(db, actor.Object, new FakeClock(Now), Mock.Of<IIntegrationSecretProtector>(),
            provider.Object, Mock.Of<IRappiOrderProcessor>(),
            Options.Create(new ApiPublicOptions()), signal ?? Mock.Of<IBackgroundWorkSignal<RappiWork>>(), new TestTenantContext());
    }

    public static async Task ProcessAsync(ApplicationDbContext db, IRappiDeliveryProvider provider, DateTime? now = null,
        CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();
        var method = typeof(RappiIntegrationWorker).GetMethod("ProcessReadyOutboxAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        await (Task)method.Invoke(null, [db, provider, new FakeClock(now ?? Now), cancellationToken, null])!;
        db.ChangeTracker.Clear();
    }
}

public sealed class RappiPostgreSqlIntegrationTheoryAttribute : TheoryAttribute
{
    public RappiPostgreSqlIntegrationTheoryAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("RUN_POSTGRES_INTEGRATION_TESTS"), "true", StringComparison.OrdinalIgnoreCase))
            Skip = "Requiere RUN_POSTGRES_INTEGRATION_TESTS=true y PostgreSQL local desechable.";
    }
}
