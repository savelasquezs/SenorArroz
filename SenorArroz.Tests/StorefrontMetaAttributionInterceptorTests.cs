using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests;

public sealed class StorefrontMetaAttributionInterceptorTests
{
    [Fact]
    public async Task Granted_consent_captures_attribution_on_checkout_and_outbox()
    {
        var accessor = ContextWithHeaders("granted");
        await using var db = CreateDb(accessor);
        var checkout = Checkout();
        var outbox = Outbox();
        db.StorefrontCheckouts.Add(checkout);
        db.PaymentNotificationOutboxMessages.Add(outbox);

        await db.SaveChangesAsync();

        Assert.True(checkout.MetaConsentGranted);
        Assert.True(outbox.MetaConsentGranted);
        Assert.Equal("Mozilla/5.0 test", checkout.MetaClientUserAgent);
        Assert.Equal("203.0.113.8", checkout.MetaClientIpAddress);
        Assert.Equal("fb.1.test.fbp", checkout.MetaFbp);
        Assert.Equal("fb.1.test.fbc", checkout.MetaFbc);
        Assert.Equal(checkout.MetaClientUserAgent, outbox.MetaClientUserAgent);
    }

    [Fact]
    public async Task Denied_consent_does_not_capture_browser_identifiers()
    {
        var accessor = ContextWithHeaders("denied");
        await using var db = CreateDb(accessor);
        var checkout = Checkout();
        db.StorefrontCheckouts.Add(checkout);

        await db.SaveChangesAsync();

        Assert.False(checkout.MetaConsentGranted);
        Assert.Null(checkout.MetaClientUserAgent);
        Assert.Null(checkout.MetaClientIpAddress);
        Assert.Null(checkout.MetaFbp);
        Assert.Null(checkout.MetaFbc);
    }

    private static HttpContextAccessor ContextWithHeaders(string consent)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Meta-Consent"] = consent;
        context.Request.Headers["X-Storefront-Client-User-Agent"] = "Mozilla/5.0 test";
        context.Request.Headers["X-Storefront-Client-Ip"] = "203.0.113.8";
        context.Request.Headers["X-Meta-Fbp"] = "fb.1.test.fbp";
        context.Request.Headers["X-Meta-Fbc"] = "fb.1.test.fbc";
        return new HttpContextAccessor { HttpContext = context };
    }

    private static ApplicationDbContext CreateDb(IHttpContextAccessor accessor)
    {
        var interceptor = new StorefrontMetaAttributionInterceptor(accessor);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(interceptor)
            .Options;
        return new ApplicationDbContext(options);
    }

    private static StorefrontCheckout Checkout() => new()
    {
        TenantId = 1,
        PublicId = Guid.NewGuid().ToString("N"),
        IdempotencyKey = Guid.NewGuid().ToString("N"),
        BranchId = 1,
        CustomerPhone = "3001234567",
        CustomerName = "Cliente",
        FulfillmentType = "delivery",
        DeliveryFee = 3_000,
        Subtotal = 40_000,
        Total = 43_000,
        ItemsJson = "[]",
        ExpiresAt = DateTime.UtcNow.AddMinutes(15),
    };

    private static PaymentNotificationOutboxMessage Outbox() => new()
    {
        TenantId = 1,
        BranchId = 1,
        OrderId = 99,
        EventType = "order_created_web_cash",
        NextAttemptAt = DateTime.UtcNow,
    };
}
