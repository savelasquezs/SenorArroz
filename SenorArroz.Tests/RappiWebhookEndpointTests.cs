using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using SenorArroz.API.Controllers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests;

/// <summary>Controller-only tests: in-memory storage, fake secret, no HTTP server or provider.</summary>
public sealed class RappiWebhookEndpointTests
{
    private const string Secret = "rappi-readiness-test-only";
    private const string Timestamp = "1792594800000";
    private const string Payload = "{\"store_id\":\"audit-local\"}";
    private static readonly DateTime Now = new(2026, 10, 21, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Valid_ping_records_diagnostics_once_without_business_work()
    {
        await using var fixture = await Fixture.CreateAsync("PING");
        var first = await fixture.SendAsync("PING", Payload, Sign(Payload));
        var repeated = await fixture.SendAsync("PING", Payload, Sign(Payload));

        Assert.IsType<OkObjectResult>(first);
        Assert.IsType<OkObjectResult>(repeated);
        var row = await fixture.Db.IntegrationWebhookEvents.SingleAsync();
        Assert.Equal("processed", row.Status);
        Assert.Equal(Now, row.ProcessedAt);
        Assert.Equal(Now, (await fixture.Db.DeliveryAppStores.SingleAsync()).LastPingAt);
        Assert.Empty(await fixture.Db.Orders.ToListAsync());
        Assert.Empty(await fixture.Db.AppPayments.ToListAsync());
        Assert.Empty(await fixture.Db.PrintJobs.ToListAsync());
        fixture.Signal.Verify(x => x.Pulse(), Times.Never);
        fixture.Provider.VerifyNoOtherCalls();
        fixture.Processor.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Altered_ping_is_unauthorized_and_does_not_write_diagnostics()
    {
        await using var fixture = await Fixture.CreateAsync("PING");
        var response = await fixture.SendAsync("PING", "{\"store_id\":\"audit-local\" }", Sign(Payload));

        Assert.IsType<UnauthorizedResult>(response);
        Assert.Empty(await fixture.Db.IntegrationWebhookEvents.ToListAsync());
        Assert.Null((await fixture.Db.DeliveryAppStores.SingleAsync()).LastPingAt);
        Assert.Null((await fixture.Db.DeliveryAppConnections.SingleAsync()).LastWebhookAt);
        fixture.Signal.Verify(x => x.Pulse(), Times.Never);
        fixture.Provider.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Valid_new_order_is_only_enqueued_by_http_handler_and_duplicate_does_not_enqueue_again()
    {
        await using var fixture = await Fixture.CreateAsync("NEW_ORDER");
        const string payload = "{\"order_id\":\"local-test-only\"}";
        Assert.IsType<OkObjectResult>(await fixture.SendAsync("NEW_ORDER", payload, Sign(payload)));
        Assert.IsType<OkObjectResult>(await fixture.SendAsync("NEW_ORDER", payload, Sign(payload)));

        var row = await fixture.Db.IntegrationWebhookEvents.SingleAsync();
        Assert.Equal("received", row.Status);
        Assert.Null(row.ProcessedAt);
        Assert.Empty(await fixture.Db.Orders.ToListAsync());
        fixture.Signal.Verify(x => x.Pulse(), Times.Once);
        fixture.Provider.VerifyNoOtherCalls();
        fixture.Processor.VerifyNoOtherCalls();
    }

    [Fact]
    public void Synthetic_vector_matches_the_independently_checked_portal_hmac()
    {
        Assert.Equal("t=1792594800000,sign=35df645b382f2b38d85e93c08b2ac77326e5dbe281e738d259a1d22cb6164f1f", Sign(Payload));
    }

    [Theory]
    [InlineData("{\"store_id\":\"unknown\"}")]
    [InlineData("{}")]
    [InlineData("[]")]
    public async Task Ping_requires_a_store_mapped_to_this_connection(string payload)
    {
        await using var fixture = await Fixture.CreateAsync("PING");
        Assert.IsType<BadRequestObjectResult>(await fixture.SendAsync("PING", payload, Sign(payload)));
        Assert.Empty(await fixture.Db.IntegrationWebhookEvents.ToListAsync());
        Assert.Null((await fixture.Db.DeliveryAppStores.SingleAsync()).LastPingAt);
        fixture.Provider.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ping_does_not_report_an_inactive_local_business_as_healthy(bool suspendTenant)
    {
        await using var fixture = await Fixture.CreateAsync("PING");
        // Even an identical, previously successful PING must evaluate current health.
        Assert.IsType<OkObjectResult>(await fixture.SendAsync("PING", Payload, Sign(Payload)));
        if (suspendTenant)
            (await fixture.Db.Tenants.SingleAsync()).Status = TenantStatus.Suspended;
        else
            (await fixture.Db.Branches.SingleAsync()).IsActive = false;
        await fixture.Db.SaveChangesAsync();
        var response = Assert.IsType<ObjectResult>(await fixture.SendAsync("PING", Payload, Sign(Payload)));
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, response.StatusCode);
        Assert.Single(await fixture.Db.IntegrationWebhookEvents.ToListAsync());
        fixture.Provider.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Ping_health_is_not_gated_by_provider_connectivity_flag()
    {
        await using var fixture = await Fixture.CreateAsync("PING");
        (await fixture.Db.DeliveryAppStores.SingleAsync()).ConnectivityEnabled = false;
        await fixture.Db.SaveChangesAsync();
        Assert.IsType<OkObjectResult>(await fixture.SendAsync("PING", Payload, Sign(Payload)));
        fixture.Provider.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Ping_rejects_ambiguous_store_identifiers()
    {
        await using var fixture = await Fixture.CreateAsync("PING");
        fixture.Db.DeliveryAppStores.Add(new DeliveryAppStore
        {
            Id = 2, ConnectionId = 1, RappiStoreId = "another-store", StoreIntegrationId = "audit-local", Name = "Ambiguous"
        });
        await fixture.Db.SaveChangesAsync();
        Assert.IsType<BadRequestObjectResult>(await fixture.SendAsync("PING", Payload, Sign(Payload)));
        Assert.Empty(await fixture.Db.IntegrationWebhookEvents.ToListAsync());
    }

    [Fact]
    public async Task Multiple_signature_header_values_are_rejected()
    {
        await using var fixture = await Fixture.CreateAsync("PING");
        Assert.IsType<UnauthorizedResult>(await fixture.SendAsync("PING", Payload, Sign(Payload), Sign(Payload)));
        Assert.Empty(await fixture.Db.IntegrationWebhookEvents.ToListAsync());
    }

    [Theory]
    [InlineData("t=unvalidated-key,")]
    [InlineData("T=unvalidated-key,")]
    [InlineData("sign=unvalidated-signature,")]
    [InlineData("t=,")]
    [InlineData("sign=,")]
    public async Task Duplicate_signature_fields_cannot_choose_an_unsigned_event_key(string prefix)
    {
        await using var fixture = await Fixture.CreateAsync("PING");
        Assert.IsType<UnauthorizedResult>(await fixture.SendAsync("PING", Payload, prefix + Sign(Payload)));
        Assert.Empty(await fixture.Db.IntegrationWebhookEvents.ToListAsync());
    }

    [Theory]
    [InlineData("production", false)]
    [InlineData("sandbox", true)]
    public async Task Canonical_tester_compatibility_is_explicitly_sandbox_only(string environment, bool accepted)
    {
        await using var fixture = await Fixture.CreateAsync("NEW_ORDER");
        (await fixture.Db.DeliveryAppConnections.SingleAsync()).Environment = environment;
        await fixture.Db.SaveChangesAsync();
        const string raw = "{\"order_id\":\"synthetic\",\"total\":12,\"online\":true,\"info\":null}";
        const string canonical = "{\"order_id\":\"synthetic\",\"total\":\"12\",\"online\":\"true\",\"info\":\"null\"}";
        var result = await fixture.SendAsync("NEW_ORDER", raw, Sign(canonical));
        if (accepted)
            Assert.IsType<OkObjectResult>(result);
        else
            Assert.IsType<UnauthorizedResult>(result);
        Assert.Equal(accepted ? 1 : 0, await fixture.Db.IntegrationWebhookEvents.CountAsync());
    }

    private static string Sign(string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        var hash = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{Timestamp}.{payload}"))).ToLowerInvariant();
        return $"t={Timestamp},sign={hash}";
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required ApplicationDbContext Db { get; init; }
        public RappiIntegrationsController Controller { get; private set; } = null!;
        public required Guid PublicId { get; init; }
        public Mock<IRappiDeliveryProvider> Provider { get; } = new(MockBehavior.Strict);
        public Mock<IRappiOrderProcessor> Processor { get; } = new(MockBehavior.Strict);
        public Mock<IBackgroundWorkSignal<RappiWork>> Signal { get; } = new();

        public static async Task<Fixture> CreateAsync(string eventType)
        {
            var context = new TestTenantContext();
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
                currentTenant: context, tenantExecutionContext: context);
            var id = Guid.NewGuid();
            db.Tenants.Add(new Tenant { Id = 1, Name = "Synthetic", Slug = "synthetic" });
            db.Branches.Add(new Branch { Id = 1, Name = "Local only" });
            var connection = new DeliveryAppConnection
            {
                Id = 1, BranchId = 1, Provider = "rappi", PublicId = id,
                IsActive = true, FinancialAppId = 1
            };
            connection.Stores.Add(new DeliveryAppStore { Id = 1, RappiStoreId = "audit-local", Name = "Local only" });
            connection.WebhookSubscriptions.Add(new DeliveryAppWebhookSubscription
            {
                Id = 1, EventType = eventType, IsActive = true, EncryptedSecret = "synthetic-envelope"
            });
            db.DeliveryAppConnections.Add(connection);
            await db.SaveChangesAsync();
            var protector = new Mock<IIntegrationSecretProtector>(MockBehavior.Strict);
            protector.Setup(x => x.Unprotect("synthetic-envelope")).Returns(Secret);
            var fixture = new Fixture { Db = db, PublicId = id };
            fixture.Controller = new RappiIntegrationsController(db, Mock.Of<ICurrentUser>(), new FakeClock(Now),
                protector.Object, fixture.Provider.Object, fixture.Processor.Object,
                Options.Create(new ApiPublicOptions()), fixture.Signal.Object, context);
            return fixture;
        }

        public async Task<IActionResult> SendAsync(string eventType, string payload, string signature,
            string? additionalSignature = null)
        {
            var http = new DefaultHttpContext();
            http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(payload));
            http.Request.Headers["Rappi-Signature"] = signature;
            if (additionalSignature is not null)
                http.Request.Headers.Append("Rappi-Signature", additionalSignature);
            Controller.ControllerContext = new ControllerContext { HttpContext = http };
            return await Controller.Webhook(PublicId, eventType, CancellationToken.None);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
