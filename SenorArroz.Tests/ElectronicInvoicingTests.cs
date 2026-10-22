using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Helpers;
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
    [Fact]
    public async Task New_requests_require_the_restaurant_identity_before_saving_a_fiscal_document()
    {
        var tenant = new TestTenantContext(); await using var db = Db(tenant);
        db.ElectronicInvoiceIntegrations.Add(Integration);
        var order = new Order { Type = OrderType.Onsite }; db.Orders.Add(order); await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<BusinessException>(() => new ElectronicInvoiceService(db, [], Mock.Of<IClock>()).RequestAsync(order, Recipient, 1, default));
        Assert.Contains("logo", error.Message); Assert.Empty(await db.ElectronicInvoiceDocuments.ToListAsync());
    }
    [Theory]
    [InlineData("invoice")]
    [InlineData("credit")]
    public async Task Provider_forces_automatic_email_off_even_for_legacy_payloads(string kind)
    {
        var secret = new Mock<IIntegrationSecretProtector>(); secret.Setup(x => x.Unprotect("protected")).Returns("test-token");
        var provider = new PlemsiInvoiceProvider(new HttpClient(new FakeHttp(async request =>
        {
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.False(payload.RootElement.GetProperty("send_email").GetBoolean());
            Assert.Equal($"/api/billing/{kind}", request.RequestUri!.AbsolutePath);
            return Json("{\"success\":true}");
        })), secret.Object);
        await provider.SubmitAsync(Integration, kind, "{\"send_email\":true}", default);
    }

    [Fact]
    public void Aggregated_discounts_span_products_without_changing_gifts_or_delivery_and_preserve_odd_quantities()
    {
        var order = new Order { Total = 6502, DeliveryFee = 500 };
        order.OrderDetails.Add(new OrderDetail { ProductId = 1, Quantity = 3, UnitPrice = 3000, Discount = 2999, Product = new Product { Name = "Principal" } });
        order.OrderDetails.Add(new OrderDetail { ProductId = 2, Quantity = 1, UnitPrice = 8000, Discount = 7999, Product = new Product { Name = "Otro" } });
        order.OrderDetails.Add(new OrderDetail { ProductId = 3, Quantity = 1, UnitPrice = 1000, Discount = 1000, Product = new Product { Name = "Regalo" } });
        using var payload = JsonDocument.Parse(Build(order));
        var items = payload.RootElement.GetProperty("items");
        Assert.Equal(0, items[0].GetProperty("line_extension_amount").GetDecimal());
        Assert.Equal(6002, items[1].GetProperty("line_extension_amount").GetDecimal());
        Assert.Equal(0, items[2].GetProperty("line_extension_amount").GetDecimal());
        Assert.Equal(500, items[3].GetProperty("line_extension_amount").GetDecimal());
        Assert.Equal(2999, order.OrderDetails.First().Discount);
        order = new Order { Total = 2999 };
        order.OrderDetails.Add(new OrderDetail { Quantity = 3, UnitPrice = 1000, Discount = 1, Product = new Product { Name = "Impar" } });
        using var odd = JsonDocument.Parse(Build(order));
        var line = odd.RootElement.GetProperty("items")[0];
        Assert.Equal(2999, line.GetProperty("price_amount").GetDecimal()); Assert.Equal(3, line.GetProperty("base_quantity").GetDecimal());
        string Build(Order value) => ElectronicInvoiceService.BuildInvoicePayload(value, Recipient, Integration, "SETT", 1, DateTime.UtcNow);
    }
    [Theory]
    [InlineData(10000, 0, 1, 47)]
    [InlineData(0, 10000, 1, 10)]
    [InlineData(5000, 5000, 1, 1)]
    [InlineData(5000, 0, 2, 47)]
    [InlineData(0, 0, 2, 1)]
    public void Payment_form_and_method_reflect_persisted_payments(int bank, int cash, int form, int method)
    {
        var order = new Order { Total = 10000, PaidInStoreCashAmount = cash };
        order.OrderDetails.Add(new OrderDetail { Quantity = 1, UnitPrice = 10000, Product = new Product { Name = "Producto" } });
        if (bank > 0) order.BankPayments.Add(new BankPayment { BankId = 1, Amount = bank });
        using var payload = JsonDocument.Parse(ElectronicInvoiceService.BuildInvoicePayload(order, Recipient, Integration, "SETT", 1, DateTime.UtcNow));
        var payment = payload.RootElement.GetProperty("payment");
        Assert.Equal(form, payment.GetProperty("payment_form_id").GetInt32());
        Assert.Equal(method, payment.GetProperty("payment_method_id").GetInt32());
    }

    [Fact]
    public void Partial_bank_deposit_is_counted_once_when_choosing_credit_or_cash_payment_form()
    {
        var order = new Order { Total = 10000 };
        order.OrderDetails.Add(new OrderDetail { Quantity = 1, UnitPrice = 10000, Product = new Product { Name = "Producto" } });
        order.Deposits.Add(new ReservationDeposit { Id = 1, Amount = 5000, IsEffective = false, BankId = 1 });
        order.BankPayments.Add(new BankPayment { Amount = 5000, BankId = 1, SourceReservationDepositId = 1 });
        using var payload = JsonDocument.Parse(ElectronicInvoiceService.BuildInvoicePayload(order, Recipient, Integration, "SETT", 1, DateTime.UtcNow));
        Assert.Equal(2, payload.RootElement.GetProperty("payment").GetProperty("payment_form_id").GetInt32());
        Assert.Equal(47, payload.RootElement.GetProperty("payment").GetProperty("payment_method_id").GetInt32());
    }
    [Theory]
    [InlineData("xml")]
    [InlineData("pdf")]
    public async Task Provider_download_reads_official_XML_envelope_and_PDF_string(string format)
    {
        var file = Encoding.UTF8.GetBytes(format == "xml" ? "<AttachedDocument/>" : "%PDF-1.7");
        var encoded = Convert.ToBase64String(file);
        var body = format == "xml" ? JsonSerializer.Serialize(new { success = true, data = new { Filename = "invoice.xml", AttachedDocumentB64 = encoded } })
            : JsonSerializer.Serialize(new { success = true, data = encoded });
        var secret = new Mock<IIntegrationSecretProtector>(); secret.Setup(x => x.Unprotect("protected")).Returns("test-token");
        var provider = new PlemsiInvoiceProvider(new HttpClient(new FakeHttp(request =>
        {
            Assert.Equal($"/api/billing/invoice/{format}/test-cufe", request.RequestUri!.AbsolutePath);
            Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(Json(body));
        })), secret.Object);
        Assert.Equal(file, await provider.DownloadAsync(Integration, "invoice", "test-cufe", format, default));
    }
    [Fact]
    public void Snapshot_identifies_deposit_bank_payments_without_mutating_order_amounts()
    {
        var order = new Order { Id = 5, Total = 23000, DeliveryFee = 3000 };
        order.BankPayments.Add(new BankPayment { BankId = 2, Amount = 5000, SourceReservationDepositId = 9 });
        using var snapshot = JsonDocument.Parse(ElectronicInvoiceService.BuildSnapshot(order, Integration));
        var persisted = snapshot.RootElement.GetProperty("order");
        Assert.Equal(23000, persisted.GetProperty("Total").GetInt32());
        Assert.Equal(9, persisted.GetProperty("bankPayments")[0].GetProperty("SourceReservationDepositId").GetInt32());
        Assert.Equal(5000, order.BankPayments.Single().Amount);
    }
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
        Assert.Equal(0, p.GetProperty("allowanceTotal").GetDecimal());
        Assert.Equal(21000, p.GetProperty("invoiceBaseTotal").GetDecimal());
        Assert.Equal(0, p.GetProperty("invoiceTaxExclusiveTotal").GetDecimal());
        Assert.Equal(3, p.GetProperty("items").GetArrayLength());
        Assert.True(p.GetProperty("items")[1].GetProperty("free_of_charge_indicator").GetBoolean());
        Assert.Empty(p.GetProperty("allTaxTotals").EnumerateArray());
        Assert.Equal("2026-10-19", p.GetProperty("date").GetString());
        Assert.False(p.GetProperty("send_email").GetBoolean());
        Assert.Empty(p.GetProperty("items")[0].GetProperty("allowance_charges").EnumerateArray());
        Assert.Equal(9000m, p.GetProperty("items")[0].GetProperty("price_amount").GetDecimal());
        Assert.Equal(100m, p.GetProperty("items")[1].GetProperty("allowance_charges")[0].GetProperty("multiplier_factor_numeric").GetDecimal());
    }

    [Fact]
    public void Discounted_untaxed_invoice_satisfies_DIAN_monetary_equations()
    {
        var order = new Order { Id = 8466, TenantId = 1, DeliveryFee = 12000, Total = 168000 };
        foreach (var (quantity, price) in new[] { (2, 66000), (2, 12000), (10, 200), (10, 100) })
            order.OrderDetails.Add(new OrderDetail { Quantity = quantity, UnitPrice = price, Discount = 750, Product = new Product { Name = "Producto" } });
        using var payload = JsonDocument.Parse(ElectronicInvoiceService.BuildInvoicePayload(order,
            Recipient with { Identification = "900507876" }, Integration, "SAV", 1, DateTime.UtcNow));
        var p = payload.RootElement;
        var lines = p.GetProperty("items").EnumerateArray().ToArray();
        var net = lines.Sum(x => x.GetProperty("line_extension_amount").GetDecimal());
        Assert.Equal(168000, net);
        Assert.Equal(net, p.GetProperty("invoiceBaseTotal").GetDecimal());
        Assert.Equal(0, p.GetProperty("invoiceTaxExclusiveTotal").GetDecimal());
        Assert.Equal(net + p.GetProperty("allTaxTotals").EnumerateArray().Sum(x => x.GetProperty("tax_amount").GetDecimal()), p.GetProperty("invoiceTaxInclusiveTotal").GetDecimal());
        Assert.Equal(0, p.GetProperty("allowanceTotal").GetDecimal());
        Assert.Equal(p.GetProperty("invoiceTaxInclusiveTotal").GetDecimal() - p.GetProperty("allowanceTotal").GetDecimal(), p.GetProperty("totalToPay").GetDecimal());
        Assert.Equal(64500, lines[0].GetProperty("price_amount").GetDecimal());
        Assert.Equal(129000, lines[0].GetProperty("line_extension_amount").GetDecimal());
        Assert.All(lines.Where(x => x.TryGetProperty("allowance_charges", out _)), line => Assert.Empty(line.GetProperty("allowance_charges").EnumerateArray()));
        Assert.Equal("7", p.GetProperty("customer").GetProperty("dv").GetString());
    }

    [Theory]
    [InlineData("900507876", "900507876", "7")]
    [InlineData("900507876-7", "900507876", "7")]
    [InlineData("1038414022", "1038414022", "9")]
    [InlineData("800197268-4", "800197268", "4")]
    [InlineData("3", "3", "2")]
    [InlineData("0", "0", "0")]
    [InlineData("4", "4", "1")]
    public void Nit_is_sent_with_a_separate_verification_digit(string identification, string number, string digit)
    {
        Assert.Equal((number, digit), ColombianTaxId.Parse(identification));
        var order = new Order { Total = 1000 };
        order.OrderDetails.Add(new OrderDetail { Quantity = 1, UnitPrice = 1000, Product = new Product { Name = "Producto" } });
        using var payload = JsonDocument.Parse(ElectronicInvoiceService.BuildInvoicePayload(order, Recipient with { Identification = identification }, Integration, "SETT", 1, DateTime.UtcNow));
        var customer = payload.RootElement.GetProperty("customer");
        Assert.Equal(number, customer.GetProperty("identification_number").GetString());
        Assert.Equal(digit, customer.GetProperty("dv").GetString());
    }

    [Theory]
    [InlineData("900507876-1")]
    [InlineData("900507876-")]
    [InlineData("900507876-77")]
    [InlineData("900507876--7")]
    [InlineData("ABC")]
    [InlineData("1234567890123456")]
    public void Invalid_nit_is_rejected_before_transmission(string identification)
    {
        Assert.Throws<BusinessException>(() => ElectronicInvoiceService.ValidateRecipient(Recipient with { Identification = identification }));
    }

    [Fact]
    public void Citizenship_card_has_no_nit_verification_digit()
    {
        var order = new Order { Total = 1000 };
        order.OrderDetails.Add(new OrderDetail { Quantity = 1, UnitPrice = 1000, Product = new Product { Name = "Producto" } });
        using var payload = JsonDocument.Parse(ElectronicInvoiceService.BuildInvoicePayload(order, Recipient with { DocumentType = 3, Identification = "71366347" }, Integration, "SETT", 1, DateTime.UtcNow));
        Assert.False(payload.RootElement.GetProperty("customer").TryGetProperty("dv", out _));
    }

    [Fact]
    public void Dian_rejection_reports_nested_rules_instead_of_generic_success()
    {
        const string response = """{"success":true,"data":{"cude":"abc","response":{"data":{"IsValid":"false","ErrorMessage":{"string":["Regla: FAU02, Rechazo: Total inválido.","Regla: FAK24, Rechazo: DV inválido.","Regla: FAU02, Rechazo: Total inválido."]},"StatusDescription":"Validación contiene errores."}}}}""";
        Assert.Equal("DIAN rechazó el documento:\nRegla: FAU02, Rechazo: Total inválido.\nRegla: FAK24, Rechazo: DV inválido.", PlemsiInvoiceProvider.DianError(response));
        Assert.Null(PlemsiInvoiceProvider.DianError("""{"success":true,"IsValid":true,"ErrorMessage":{"string":["Notificación"]}}"""));
    }

    [Theory]
    [InlineData(132000, 750)]
    [InlineData(24000, 750)]
    [InlineData(2000, 750)]
    [InlineData(1000, 750)]
    public void Discount_is_included_in_the_final_price_without_fractional_percentage(int gross, int discount)
    {
        var order = new Order { Total = gross - discount };
        order.OrderDetails.Add(new OrderDetail { Quantity = 1, UnitPrice = gross, Discount = discount, Product = new Product { Name = "Producto" } });
        using var payload = JsonDocument.Parse(ElectronicInvoiceService.BuildInvoicePayload(order, Recipient, Integration, "SETT", 1, DateTime.UtcNow));
        var line = payload.RootElement.GetProperty("items")[0];
        Assert.Empty(line.GetProperty("allowance_charges").EnumerateArray());
        Assert.Equal(gross - discount, line.GetProperty("price_amount").GetDecimal());
    }

    [Theory]
    [InlineData("{\"success\":false,\"info\":\"Internal server error\",\"data\":[{\"field\":\"number\",\"reason\":\"unknown\"}]}")]
    [InlineData("{\"success\":false,\"info\":\"Error at line 0: Invalid data structure\",\"data\":[]}")]
    [InlineData("{\"success\":false,\"info\":\"Error at line 0: Invalid data structure\",\"data\":[{\"field\":\"number\"}]}")]
    [InlineData("{\"success\":true,\"info\":\"Error at line 0: Invalid data structure\",\"data\":[{\"field\":\"number\",\"reason\":\"required\"}]}")]
    [InlineData("{\"success\":false,\"cufe\":\"abc\",\"info\":\"Error at line 0: Invalid data structure\",\"data\":[{\"field\":\"number\",\"reason\":\"required\"}]}")]
    [InlineData("<html>500</html>")]
    public void Unknown_provider_failures_are_not_structural_rejections(string response)
    {
        Assert.Null(PlemsiInvoiceProvider.DataValidationError(response));
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
        db.ElectronicInvoiceBrandings.Add(new ElectronicInvoiceBranding { DisplayName = "Restaurante", Logo = InvoiceDeliveryTests.Logo });
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
