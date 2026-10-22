using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Integrations;

namespace SenorArroz.Tests;

public sealed class ElectronicInvoicingTests
{
    private static BillingRecipientDto Recipient => new() { DocumentType = 6, Identification = "900123456", Name = "Cliente SA", Email = "cliente@example.com" };
    private static ElectronicInvoiceIntegration Integration => new()
    {
        TaxId = "900111222", LegalName = "Restaurante", EncryptedToken = "protected", IsActive = true,
        Resolution = "18760000001", Prefix = "SETT", NextNumber = 1, LastNumber = 1000,
        CreditPrefix = "NC", TestedAt = DateTime.UtcNow, ValidUntil = DateTime.UtcNow.AddDays(30)
    };

    [Fact]
    public void Payload_uses_persisted_prices_discounts_delivery_and_gifts_without_tax()
    {
        var order = new Order { Id = 5, TenantId = 1, DeliveryFee = 3000, Total = 21000 };
        order.OrderDetails.Add(new OrderDetail { ProductId = 1, Quantity = 2, UnitPrice = 10000, Discount = 2000,
            Product = new Product { Name = "Arroz", Price = 99000 } });
        order.OrderDetails.Add(new OrderDetail { ProductId = 2, Quantity = 1, UnitPrice = 2000, Discount = 2000,
            Product = new Product { Name = "Regalo" } });
        using var payload = JsonDocument.Parse(ElectronicInvoiceService.BuildInvoicePayload(order, Recipient, Integration, "SETT", 1, new DateTime(2026, 10, 20, 2, 0, 0, DateTimeKind.Utc)));
        var p = payload.RootElement;
        Assert.Equal(21000, p.GetProperty("totalToPay").GetDecimal());
        Assert.Equal(4000, p.GetProperty("allowanceTotal").GetDecimal());
        Assert.Equal(25000, p.GetProperty("invoiceBaseTotal").GetDecimal());
        Assert.Equal(3, p.GetProperty("items").GetArrayLength());
        Assert.True(p.GetProperty("items")[1].GetProperty("free_of_charge_indicator").GetBoolean());
        Assert.Empty(p.GetProperty("allTaxTotals").EnumerateArray());
        Assert.Equal("2026-10-19", p.GetProperty("date").GetString());
        Assert.False(p.GetProperty("send_email").GetBoolean());
    }

    [Fact]
    public void Payload_rejects_monetary_mismatch()
    {
        var order = new Order { Total = 15000 };
        order.OrderDetails.Add(new OrderDetail { Quantity = 1, UnitPrice = 10000, Product = new Product { Name = "Arroz" } });
        Assert.Throws<BusinessException>(() => ElectronicInvoiceService.BuildInvoicePayload(order, Recipient, Integration, "SETT", 1, DateTime.UtcNow));
    }

    [Fact]
    public void Missing_email_is_valid_and_extra_buyer_tax_fields_are_unnecessary()
    {
        ElectronicInvoiceService.ValidateRecipient(Recipient with { Email = null });
        Assert.Throws<BusinessException>(() => ElectronicInvoiceService.ValidateRecipient(Recipient with { Name = " " }));
        Assert.Throws<BusinessException>(() => ElectronicInvoiceService.ValidateRecipient(Recipient with { Email = "invalid" }));
    }

    [Theory]
    [InlineData("{\"success\":true,\"cufe\":\"abc\"}", null)]
    [InlineData("{\"data\":{\"ResponseDian\":{\"IsValid\":true}}}", true)]
    [InlineData("{\"data\":{\"is_valid\":\"false\"}}", false)]
    public void Provider_success_never_substitutes_DIAN_validation(string json, bool? expected)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Equal(expected, PlemsiInvoiceProvider.DianValidation(document.RootElement));
    }

    [Fact]
    public void Production_requires_explicit_readiness_and_current_numbering()
    {
        var integration = Integration; integration.Environment = "production";
        Assert.Throws<BusinessException>(() => ElectronicInvoiceService.EnsureReady(integration));
        integration.ProductionReady = true;
        ElectronicInvoiceService.EnsureReady(integration);
        integration.NextNumber = 1001;
        Assert.Throws<BusinessException>(() => ElectronicInvoiceService.EnsureReady(integration));
    }

    [Fact]
    public async Task Reservation_request_is_deferred_idempotent_and_keeps_recipient_snapshot()
    {
        var tenant = new TestTenantContext();
        await using var db = Db(tenant);
        db.ElectronicInvoiceIntegrations.Add(Integration);
        var order = new Order { Type = OrderType.Reservation, Status = OrderStatus.Taken };
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        var service = new ElectronicInvoiceService(db, [], Mock.Of<IClock>());
        var first = await service.RequestAsync(order, Recipient, 5, default);
        var replay = await service.RequestAsync(order, Recipient with { Name = "Nuevo nombre" }, 5, default);
        Assert.Equal("scheduled", first.Status);
        Assert.Equal(first.Id, replay.Id);
        Assert.Null(first.Number);
        Assert.Equal("Cliente SA", JsonSerializer.Deserialize<BillingRecipientDto>(first.RecipientJson)!.Name);
        Assert.Equal("pending", first.EmailStatus);
    }

    [Fact]
    public async Task Billing_entities_are_invisible_and_unwritable_across_tenants()
    {
        var tenant = new TestTenantContext();
        await using var db = Db(tenant);
        using (tenant.BeginSystemScope())
        {
            db.ElectronicInvoiceIntegrations.AddRange(new ElectronicInvoiceIntegration { TenantId = 1 }, new ElectronicInvoiceIntegration { TenantId = 2 });
            db.CustomerBillingRecipients.AddRange(new CustomerBillingRecipient { TenantId = 1, CustomerId = 1, Name = "Uno" }, new CustomerBillingRecipient { TenantId = 2, CustomerId = 2, Name = "Dos" });
            db.ElectronicInvoiceDocuments.AddRange(new ElectronicInvoiceDocument { TenantId = 1, OrderId = 1 }, new ElectronicInvoiceDocument { TenantId = 2, OrderId = 2 });
            await db.SaveChangesAsync();
        }
        db.ChangeTracker.Clear();
        Assert.Single(await db.ElectronicInvoiceIntegrations.ToListAsync());
        Assert.Single(await db.CustomerBillingRecipients.ToListAsync());
        Assert.Single(await db.ElectronicInvoiceDocuments.ToListAsync());
        db.CustomerBillingRecipients.Add(new CustomerBillingRecipient { TenantId = 2 });
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public void Optional_invoice_field_preserves_legacy_serialized_request()
    {
        var dto = new CreateOrderDto();
        Assert.DoesNotContain("ElectronicInvoice", JsonSerializer.Serialize(dto));
        dto.ElectronicInvoice = Recipient;
        Assert.Contains("ElectronicInvoice", JsonSerializer.Serialize(dto));
    }

    [Fact]
    public async Task Email_sync_preserves_other_company_addresses()
    {
        string? updated = null;
        var handler = new FakeHttp(async request =>
        {
            if (request.Method == HttpMethod.Get) return Json("{\"data\":{\"email\":{\"general\":\"general@example.com\",\"other\":\"other@example.com\"}}}");
            updated = await request.Content!.ReadAsStringAsync();
            return Json("{\"success\":true}");
        });
        var secrets = new Mock<IIntegrationSecretProtector>(); secrets.Setup(x => x.Unprotect(It.IsAny<string>())).Returns("test-token");
        var provider = new PlemsiInvoiceProvider(new HttpClient(handler), secrets.Object);
        var integration = Integration; integration.BillingEmail = "billing@example.com";
        await provider.SyncEmailAsync(integration, default);
        using var result = JsonDocument.Parse(updated!);
        var email = result.RootElement.GetProperty("email");
        Assert.Equal("general@example.com", email.GetProperty("general").GetString());
        Assert.Equal("other@example.com", email.GetProperty("other").GetString());
        Assert.Equal("billing@example.com", email.GetProperty("billing").GetString());
    }

    [Theory]
    [InlineData("OTHER", 1, 100, "2030-01-01", 1)]
    [InlineData("SETT", 5, 100, "2030-01-01", 5)]
    [InlineData("SETT", 1, 50, "2030-01-01", 1)]
    [InlineData("SETT", 1, 100, "2020-01-01", 1)]
    [InlineData("SETT", 1, 100, "2030-01-01", 20)]
    public void Numbering_rejects_unmatched_expired_or_already_used_ranges(string prefix, long from, long to, string expiry, long next)
    {
        var integration = Integration; integration.LastNumber = 100; integration.ValidUntil = new DateTime(2029, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var rows = new[] { new InvoiceNumbering(integration.Resolution, prefix, from, to, DateTime.Parse(expiry), next) };
        Assert.Throws<BusinessException>(() => InvoiceNumbering.Validate(integration, rows, null, new DateTime(2026, 10, 20)));
    }

    [Fact]
    public void Numbering_never_defaults_to_range_start_without_confirmation()
    {
        using var json = JsonDocument.Parse("""{"data":[{"resolution":"18760000001","prefix":"SETT","from":1,"to":100,"date_to":"2030-01-01"}]}""");
        var rows = InvoiceNumbering.Read(json.RootElement);
        Assert.Null(Assert.Single(rows).NextNumber);
        var integration = Integration; integration.LastNumber = 100;
        Assert.Throws<BusinessException>(() => InvoiceNumbering.Validate(integration, rows, null, DateTime.UtcNow));
        using var remaining = JsonDocument.Parse("""{"data":{"next_number":10}}""");
        integration.NextNumber = 10;
        InvoiceNumbering.Validate(integration, rows, remaining.RootElement, DateTime.UtcNow);
        integration.NextNumber = 9;
        Assert.Throws<BusinessException>(() => InvoiceNumbering.Validate(integration, rows, remaining.RootElement, DateTime.UtcNow));
    }

    [Theory]
    [InlineData("{\"data\":{\"dni\":{\"type\":\"NIT\",\"number\":\"1038414022\",\"verificationDigit\":\"9\"}}}", "1038414022")]
    [InlineData("{\"data\":{\"dni\":{\"number\":1038414022}}}", "1038414022")]
    [InlineData("{\"data\":{\"identification_number\":\"900123456\"}}", "900123456")]
    [InlineData("{\"data\":{\"invoice\":{\"number\":\"123\"}}}", null)]
    public void Company_identity_accepts_live_dni_contract_without_confusing_other_numbers(string json, string? taxId)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Equal(taxId, PlemsiInvoiceProvider.CompanyTaxId(document.RootElement));
    }

    [Theory]
    [InlineData("{\"data\":[{\"invoice\":{\"prefix\":\"SAV\",\"number\":40},\"remaining\":999959}]}", 41L)]
    [InlineData("{\"data\":{\"lastNumberUsed\":40}}", 41L)]
    [InlineData("{\"data\":[{\"invoice\":{\"number\":null},\"remaining\":null}]}", null)]
    public void Numbering_accepts_live_remaining_contract_and_keeps_null_unknown(string json, long? next)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Equal(next, InvoiceNumbering.ReadNext(document.RootElement));
    }

    [Fact]
    public void Unknown_provider_counter_requires_explicit_confirmation_and_never_overrides_known_floor()
    {
        var integration = Integration; integration.LastNumber = 100;
        var rows = new[] { new InvoiceNumbering(integration.Resolution, integration.Prefix, 1, 100, DateTime.UtcNow.AddDays(30), null) };
        Assert.Throws<BusinessException>(() => InvoiceNumbering.Validate(integration, rows, null, DateTime.UtcNow));
        InvoiceNumbering.Validate(integration, rows, null, DateTime.UtcNow, numberingConfirmed: true);
        rows[0] = rows[0] with { NextNumber = 20 };
        Assert.Throws<BusinessException>(() => InvoiceNumbering.Validate(integration, rows, null, DateTime.UtcNow, numberingConfirmed: true));
        integration.NextNumber = 101;
        Assert.Throws<BusinessException>(() => InvoiceNumbering.Validate(integration, rows, null, DateTime.UtcNow, numberingConfirmed: true));
    }

    [Fact]
    public async Task Production_mutations_are_blocked_without_contacting_provider()
    {
        var calls = 0;
        var provider = new PlemsiInvoiceProvider(new HttpClient(new FakeHttp(_ => { calls++; return Task.FromResult(Json("{\"data\":{\"email\":{\"general\":\"test@example.com\"}}}")); })), Mock.Of<IIntegrationSecretProtector>());
        var integration = Integration; integration.Environment = "production"; integration.ProductionReady = true;
        await Assert.ThrowsAsync<BusinessException>(() => provider.SubmitAsync(integration, "invoice", "{}", default));
        await Assert.ThrowsAsync<BusinessException>(() => provider.SyncEmailAsync(integration, default));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Production_request_is_blocked_even_if_integration_was_previously_active()
    {
        var tenant = new TestTenantContext(); await using var db = Db(tenant);
        var integration = Integration; integration.Environment = "production"; integration.ProductionReady = true;
        db.ElectronicInvoiceIntegrations.Add(integration); var order = new Order { Type = OrderType.Onsite }; db.Orders.Add(order); await db.SaveChangesAsync();
        Assert.Equal("production", (await db.ElectronicInvoiceIntegrations.SingleAsync()).Environment);
        Assert.Empty(await db.ElectronicInvoiceDocuments.ToListAsync());
        await Assert.ThrowsAsync<BusinessException>(() => new ElectronicInvoiceService(db, [], Mock.Of<IClock>()).RequestAsync(order, Recipient, 1, default));
        Assert.Empty(db.ElectronicInvoiceDocuments);
    }

    [Fact]
    public void Authentication_error_has_actionable_message_without_returning_token()
    {
        var error = new PlemsiRequestException(HttpStatusCode.BadRequest, "{\"error\":\"Api Auth Validation Error\"}");
        Assert.Contains("Verifica con soporte", error.Message);
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private sealed class FakeHttp(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request);
    }
    private static ApplicationDbContext Db(TestTenantContext tenant) => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, currentTenant: tenant, tenantExecutionContext: tenant);
}
