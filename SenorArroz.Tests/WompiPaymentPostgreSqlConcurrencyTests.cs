using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Integrations;
using Testcontainers.PostgreSql;

namespace SenorArroz.Tests;

public sealed class WompiPaymentPostgreSqlConcurrencyTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 9, 2, 15, 0, 0, DateTimeKind.Utc);
    private const string EventsSecret = "test_events_secret";
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private DbContextOptions<ApplicationDbContext> _options = null!;
    private StorefrontCheckout _checkout = null!;
    private WompiPaymentAttempt _attempt = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        await using var db = CreateDb();
        await db.Database.EnsureCreatedAsync();
        await SeedAsync(db);
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Webhook_and_return_synchronization_create_one_order_and_one_payment()
    {
        var payload = Webhook(_attempt, "tx-concurrent-1", 1788361200000);
        var transactionResponse = JsonSerializer.Serialize(new
        {
            data = new
            {
                id = "tx-concurrent-1",
                reference = _attempt.Reference,
                status = "APPROVED",
                amount_in_cents = _attempt.ExpectedAmountInCents,
                currency = "COP",
                payment_method_type = "CARD",
            },
        });

        await using var webhookDb = CreateDb();
        await using var returnDb = CreateDb();
        var webhookService = CreateService(webhookDb, new StaticResponseHandler(HttpStatusCode.OK, transactionResponse));
        var returnService = CreateService(returnDb, new StaticResponseHandler(HttpStatusCode.OK, transactionResponse));

        var webhookTask = webhookService.ProcessWebhookAsync("sandbox", payload, null, CancellationToken.None);
        var returnTask = returnService.SynchronizeCheckoutTransactionAsync(1, _checkout.PublicId, "tx-concurrent-1", CancellationToken.None);
        await Task.WhenAll(webhookTask, returnTask);

        Assert.True(webhookTask.Result.Accepted);
        Assert.NotNull(returnTask.Result);
        await using var verificationDb = CreateDb();
        Assert.Single(await verificationDb.Orders.AsNoTracking().ToListAsync());
        Assert.Single(await verificationDb.AppPayments.AsNoTracking().ToListAsync());
        Assert.Single(await verificationDb.PaymentNotificationOutboxMessages.AsNoTracking().ToListAsync());
        Assert.Single(await verificationDb.WompiProviderTransactions.AsNoTracking().ToListAsync());
        Assert.Single(await verificationDb.WompiWebhookEvents.AsNoTracking().ToListAsync());
    }

    private ApplicationDbContext CreateDb() => new(_options);

    private static WompiPaymentService CreateService(ApplicationDbContext db, HttpMessageHandler handler)
    {
        var protector = new Mock<IIntegrationSecretProtector>();
        protector.Setup(x => x.Unprotect(It.IsAny<string>())).Returns((string value) => value);
        return new WompiPaymentService(
            db,
            protector.Object,
            new FakeClock(Now),
            Mock.Of<IPaymentReviewNotificationService>(),
            new PostgresWompiPaymentAttemptLock(db),
            new HttpClient(handler),
            NullLogger<WompiPaymentService>.Instance);
    }

    private async Task SeedAsync(ApplicationDbContext db)
    {
        var branch = new Branch
        {
            Id = 1,
            Name = "Sucursal 1",
            Address = "-",
            Phone1 = "3000000000",
        };
        db.Branches.Add(branch);
        db.Users.Add(new User
        {
            Id = 1,
            BranchId = 1,
            Name = "Web",
            Email = "web@test.local",
            Phone = "3000000001",
            PasswordHash = "x",
        });
        db.Banks.Add(new Bank { Id = 1, BranchId = 1, Name = "Bancolombia", Active = true });
        db.Apps.Add(new App { Id = 5, BankId = 1, Name = "Wompi", Active = true });
        db.ProductCategories.Add(new ProductCategory { Id = 2, BranchId = 1, Name = "Arroces" });
        db.Products.Add(new Product { Id = 99, CategoryId = 2, Name = "Arroz", Price = 18_000 });
        var integration = new WompiPaymentIntegration
        {
            Id = 20,
            TenantId = 1,
            BranchId = 1,
            FinancialAppId = 5,
            ActiveEnvironment = "sandbox",
            IsEnabled = true,
            EstimatedCommissionRate = 0.029m,
            SandboxPublicKey = "pub_test_public",
            SandboxEncryptedIntegritySecret = "test_integrity_secret",
            SandboxEncryptedEventsSecret = EventsSecret,
        };
        db.WompiPaymentIntegrations.Add(integration);
        _checkout = new StorefrontCheckout
        {
            TenantId = 1,
            PublicId = Guid.NewGuid().ToString("N"),
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            BranchId = 1,
            CustomerPhone = "3001234567",
            CustomerName = "Cliente web",
            FulfillmentType = "delivery",
            FormattedAddress = "Calle 10 # 20-30",
            Latitude = 6.25m,
            Longitude = -75.56m,
            DeliveryFee = 4_000,
            Subtotal = 18_000,
            Total = 22_000,
            ItemsJson = JsonSerializer.Serialize(new[] { new StorefrontCheckoutLine(99, 1, 18_000, 0, 18_000, null) }),
            Status = "pending",
            ExpiresAt = Now.AddMinutes(15),
        };
        db.StorefrontCheckouts.Add(_checkout);
        await db.SaveChangesAsync();
        branch.StorefrontTakenByUserId = 1;
        await db.SaveChangesAsync();

        var service = CreateService(db, new StaticResponseHandler(HttpStatusCode.NotFound, string.Empty));
        service.CreateCheckoutAttempt(_checkout, integration, Now);
        await db.SaveChangesAsync();
        _attempt = await db.WompiPaymentAttempts.AsNoTracking().SingleAsync();
    }

    private static string Webhook(WompiPaymentAttempt attempt, string transactionId, long timestamp)
    {
        var checksum = Sha256($"{transactionId}APPROVED{attempt.ExpectedAmountInCents}{timestamp}{EventsSecret}");
        return JsonSerializer.Serialize(new
        {
            @event = "transaction.updated",
            data = new
            {
                transaction = new
                {
                    id = transactionId,
                    reference = attempt.Reference,
                    status = "APPROVED",
                    amount_in_cents = attempt.ExpectedAmountInCents,
                    currency = "COP",
                    payment_method_type = "CARD",
                },
            },
            signature = new
            {
                properties = new[] { "transaction.id", "transaction.status", "transaction.amount_in_cents" },
                checksum,
            },
            timestamp,
        });
    }

    private static string Sha256(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class StaticResponseHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }
}
