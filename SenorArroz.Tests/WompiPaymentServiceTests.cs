using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Integrations;

namespace SenorArroz.Tests;

public sealed class WompiPaymentServiceTests
{
    private static readonly DateTime Now = new(2026, 9, 1, 16, 0, 0, DateTimeKind.Utc);
    private const string IntegritySecret = "test_integrity_secret";
    private const string EventsSecret = "test_events_secret";

    [Fact]
    public async Task CreateAttempt_signs_exact_checkout_expiration()
    {
        await using var db = CreateDb(nameof(CreateAttempt_signs_exact_checkout_expiration));
        var setup = await SeedAsync(db);
        var service = CreateService(db, new FakeClock(Now));

        var checkout = service.CreateAttempt(setup.Order, setup.Integration, Now);

        Assert.Equal("2026-09-01T16:15:00.000Z", checkout.ExpiresAt);
        Assert.Equal(
            Sha256($"{checkout.Reference}{checkout.AmountInCents}COP{checkout.ExpiresAt}{IntegritySecret}"),
            checkout.IntegritySignature);
        Assert.Equal("pub_test_public", checkout.PublicKey);
        Assert.Equal("sandbox", checkout.Environment);
    }

    [Fact]
    public async Task Approved_webhook_creates_financial_payment_and_releases_order_once()
    {
        await using var db = CreateDb(nameof(Approved_webhook_creates_financial_payment_and_releases_order_once));
        var setup = await SeedAsync(db);
        var clock = new FakeClock(Now);
        var service = CreateService(db, clock);
        service.CreateAttempt(setup.Order, setup.Integration, Now);
        await db.SaveChangesAsync();
        var attempt = await db.WompiPaymentAttempts.SingleAsync();
        setup.Integration.SandboxEncryptedEventsSecret = "test_events_rotated";
        await db.SaveChangesAsync();
        var payload = Webhook(attempt, "tx-approved-1", "APPROVED", 1788278400000);

        var first = await service.ProcessWebhookAsync("sandbox", payload, null, CancellationToken.None);
        var duplicate = await service.ProcessWebhookAsync("sandbox", payload, null, CancellationToken.None);
        var repeatedObservation = await service.ProcessWebhookAsync(
            "sandbox",
            Webhook(attempt, "tx-approved-1", "APPROVED", 1788278401000),
            null,
            CancellationToken.None);

        Assert.True(first.Accepted);
        Assert.False(first.RequiresManualReview);
        Assert.True(duplicate.Duplicate);
        Assert.False(repeatedObservation.RequiresManualReview);
        Assert.Equal(OrderStatus.Taken, setup.Order.Status);
        Assert.Equal(PaymentAttemptStatus.Approved, attempt.Status);
        var payment = await db.AppPayments.SingleAsync();
        Assert.Equal(setup.Order.Total, payment.Amount);
        Assert.Equal(0.029m, payment.EstimatedCommissionRate);
        Assert.Equal(638m, payment.EstimatedCommissionAmount);
        Assert.Equal(21_362m, payment.ExpectedNetAmount);
        Assert.Single(await db.PaymentNotificationOutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task Late_approved_webhook_requires_manual_review_without_releasing_order()
    {
        await using var db = CreateDb(nameof(Late_approved_webhook_requires_manual_review_without_releasing_order));
        var setup = await SeedAsync(db);
        var clock = new FakeClock(Now);
        var notifications = new Mock<IPaymentReviewNotificationService>();
        var service = CreateService(db, clock, notifications);
        service.CreateAttempt(setup.Order, setup.Integration, Now);
        await db.SaveChangesAsync();
        var attempt = await db.WompiPaymentAttempts.SingleAsync();
        clock.UtcNow = Now.AddMinutes(16);

        var result = await service.ProcessWebhookAsync(
            "sandbox",
            Webhook(attempt, "tx-late-1", "APPROVED", 1788279360000),
            null,
            CancellationToken.None);

        Assert.True(result.RequiresManualReview);
        Assert.Equal(OrderStatus.AwaitingPayment, setup.Order.Status);
        Assert.Empty(await db.AppPayments.ToListAsync());
        Assert.Equal(PaymentAttemptStatus.ReviewRequired, attempt.Status);
        notifications.Verify(x => x.NotifyReviewRequiredAsync(1, setup.Order.Id, attempt.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Invalid_webhook_signature_does_not_change_payment()
    {
        await using var db = CreateDb(nameof(Invalid_webhook_signature_does_not_change_payment));
        var setup = await SeedAsync(db);
        var service = CreateService(db, new FakeClock(Now));
        service.CreateAttempt(setup.Order, setup.Integration, Now);
        await db.SaveChangesAsync();
        var attempt = await db.WompiPaymentAttempts.SingleAsync();

        var result = await service.ProcessWebhookAsync(
            "sandbox",
            Webhook(attempt, "tx-invalid-1", "APPROVED", 1788278400000),
            "invalid",
            CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Equal(OrderStatus.AwaitingPayment, setup.Order.Status);
        Assert.Empty(await db.AppPayments.ToListAsync());
    }

    private static ApplicationDbContext CreateDb(string name) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options);

    private static WompiPaymentService CreateService(
        ApplicationDbContext db,
        FakeClock clock,
        Mock<IPaymentReviewNotificationService>? notifications = null)
    {
        var protector = new Mock<IIntegrationSecretProtector>();
        protector.Setup(x => x.Unprotect(It.IsAny<string>())).Returns((string value) => value);
        return new WompiPaymentService(
            db,
            protector.Object,
            clock,
            (notifications ?? new Mock<IPaymentReviewNotificationService>()).Object,
            new HttpClient(new StubHttpHandler()),
            NullLogger<WompiPaymentService>.Instance);
    }

    private static async Task<(Order Order, WompiPaymentIntegration Integration)> SeedAsync(ApplicationDbContext db)
    {
        var branch = new Branch { Id = 1, Name = "Sucursal 1", Address = "-", Phone1 = "-" };
        var user = new User { Id = 1, BranchId = 1, Name = "Web", Email = "web@test.local", Phone = "1", PasswordHash = "x", Branch = branch };
        var bank = new Bank { Id = 1, BranchId = 1, Name = "Bancolombia", Active = true, Branch = branch };
        var app = new App { Id = 5, BankId = 1, Name = "Wompi", Active = true, Bank = bank };
        var order = new Order
        {
            Id = 10,
            BranchId = 1,
            TakenById = 1,
            Branch = branch,
            TakenBy = user,
            Type = OrderType.Delivery,
            Status = OrderStatus.AwaitingPayment,
            Subtotal = 18_000,
            DeliveryFee = 4_000,
            Total = 22_000,
        };
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
            SandboxEncryptedIntegritySecret = IntegritySecret,
            SandboxEncryptedEventsSecret = EventsSecret,
            Branch = branch,
            FinancialApp = app,
        };
        db.AddRange(branch, user, bank, app, order, integration);
        await db.SaveChangesAsync();
        return (order, integration);
    }

    private static string Webhook(WompiPaymentAttempt attempt, string transactionId, string status, long timestamp)
    {
        var checksum = Sha256($"{transactionId}{status}{attempt.ExpectedAmountInCents}{timestamp}{EventsSecret}");
        return JsonSerializer.Serialize(new
        {
            @event = "transaction.updated",
            data = new
            {
                transaction = new
                {
                    id = transactionId,
                    reference = attempt.Reference,
                    status,
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

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
