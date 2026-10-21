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

        public async Task<IActionResult> SendAsync(string eventType, string payload, string signature)
        {
            var http = new DefaultHttpContext();
            http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(payload));
            http.Request.Headers["Rappi-Signature"] = signature;
            Controller.ControllerContext = new ControllerContext { HttpContext = http };
            return await Controller.Webhook(PublicId, eventType, CancellationToken.None);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
