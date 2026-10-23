using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Moq;
using Npgsql;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Integrations;
using SenorArroz.API.Controllers;
using Microsoft.Extensions.Configuration;
using SenorArroz.Infrastructure.Services;
using SenorArroz.Infrastructure.Helpers;

namespace SenorArroz.Tests;

public sealed class ElectronicInvoicingPostgreSqlTests : IAsyncLifetime
{
    private string connection = "", database = "", runtimeRole = "";
    private readonly TestTenantContext tenant = new();
    private static string? AdminConnection => Environment.GetEnvironmentVariable("INVOICE_TEST_POSTGRES");
    private ApplicationDbContext Db(bool restricted = false)
    {
        var cs = new NpgsqlConnectionStringBuilder(connection);
        if (restricted) cs.Username = runtimeRole;
        return new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(cs.ConnectionString)
            .AddInterceptors(new TenantDbConnectionInterceptor(tenant, tenant), new TenantDbCommandInterceptor(tenant, tenant)).Options,
            currentTenant: tenant, tenantExecutionContext: tenant);
    }
    private static string Script()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "SenorArroz.Infrastructure", "Scripts", "add_electronic_invoicing.sql");
            if (File.Exists(path)) return File.ReadAllText(path) + "\n" + File.ReadAllText(Path.Combine(Path.GetDirectoryName(path)!, "add_invoice_delivery.sql"))
                + "\n" + File.ReadAllText(Path.Combine(Path.GetDirectoryName(path)!, "add_invoice_branding.sql"))
                + "\n" + File.ReadAllText(Path.Combine(Path.GetDirectoryName(path)!, "add_billing_recipient_directory.sql"));
        }
        throw new FileNotFoundException("add_electronic_invoicing.sql");
    }
    public async Task InitializeAsync()
    {
        if (AdminConnection is null) return;
        database = "invoice_test_" + Guid.NewGuid().ToString("N"); runtimeRole = database + "_runtime";
        await using var admin = new NpgsqlConnection(AdminConnection); await admin.OpenAsync();
        var password = new NpgsqlConnectionStringBuilder(AdminConnection).Password;
        var passwordClause = string.IsNullOrEmpty(password) ? "" : " PASSWORD '" + password.Replace("'", "''") + "'";
        await new NpgsqlCommand($"CREATE DATABASE {database}; CREATE ROLE {runtimeRole} LOGIN NOSUPERUSER NOBYPASSRLS{passwordClause}", admin).ExecuteNonQueryAsync();
        var cs = new NpgsqlConnectionStringBuilder(AdminConnection) { Database = database };
        connection = cs.ConnectionString;
        await using var db = Db();
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE SCHEMA IF NOT EXISTS app;
            CREATE OR REPLACE FUNCTION app.current_tenant_id() RETURNS integer LANGUAGE sql STABLE AS $$
              SELECT NULLIF(current_setting('app.current_tenant_id',true),'')::integer $$;
            CREATE OR REPLACE FUNCTION app.is_system_scope() RETURNS boolean LANGUAGE sql STABLE AS $$
              SELECT COALESCE(current_setting('app.system_scope',true),'off')='on' $$;
            """);
        using (tenant.BeginSystemScope())
        {
            db.Tenants.AddRange(new Tenant { Id = 1, Name = "Uno", Slug = "uno" }, new Tenant { Id = 2, Name = "Dos", Slug = "dos" });
            await db.SaveChangesAsync();
            db.Branches.AddRange(new Branch { Id = 1, TenantId = 1, Name = "Uno", Address = "A", Phone1 = "1" }, new Branch { Id = 2, TenantId = 2, Name = "Dos", Address = "B", Phone1 = "2" });
            await db.SaveChangesAsync();
            db.Users.Add(new User { Id = 1, TenantId = 1, BranchId = 1, Name = "Cajero", Email = "invoice@test.local", PasswordHash = "test", Role = UserRole.Cashier });
            db.ProductCategories.Add(new ProductCategory { Id = 1, TenantId = 1, BranchId = 1, Name = "Arroces" });
            db.Customers.AddRange(new Customer { Id = 1, TenantId = 1, BranchId = 1, Name = "Uno", Phone1 = "3001111111" }, new Customer { Id = 2, TenantId = 2, BranchId = 2, Name = "Dos", Phone1 = "3002222222" });
            await db.SaveChangesAsync();
            db.Products.Add(new Product { Id = 1, TenantId = 1, CategoryId = 1, Name = "Arroz", Price = 10000 });
            db.Orders.AddRange(new Order { Id = 1, TenantId = 1, BranchId = 1, TakenById = 1, Total = 10000, Subtotal = 10000 }, new Order { Id = 2, TenantId = 1, BranchId = 1, TakenById = 1, Total = 10000, Subtotal = 10000 });
            db.ElectronicInvoiceIntegrations.Add(new ElectronicInvoiceIntegration { Id = 1, TenantId = 1, TaxId = "123", LegalName = "Restaurante", EncryptedToken = "test", IsActive = true, Resolution = "123", Prefix = "SETT", LastNumber = 100, ValidUntil = DateTime.UtcNow.AddDays(30), TestedAt = DateTime.UtcNow });
            db.ElectronicInvoiceBrandings.Add(new ElectronicInvoiceBranding { Id = 1, TenantId = 1, DisplayName = "Restaurante", Logo = InvoiceDeliveryTests.Logo });
            await db.SaveChangesAsync();
            db.OrderDetails.AddRange(new OrderDetail { TenantId = 1, OrderId = 1, ProductId = 1, Quantity = 1, UnitPrice = 10000, Subtotal = 10000 }, new OrderDetail { TenantId = 1, OrderId = 2, ProductId = 1, Quantity = 1, UnitPrice = 10000, Subtotal = 10000 });
            await db.SaveChangesAsync();
        }
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = Script();
        await command.ExecuteNonQueryAsync();
        await command.ExecuteNonQueryAsync();
        command.CommandText = $"GRANT USAGE ON SCHEMA public,app TO {runtimeRole}; GRANT ALL ON ALL TABLES IN SCHEMA public TO {runtimeRole}; GRANT USAGE,SELECT ON ALL SEQUENCES IN SCHEMA public TO {runtimeRole}; GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA app TO {runtimeRole}";
        await command.ExecuteNonQueryAsync();
    }
    public async Task DisposeAsync()
    {
        if (AdminConnection is null) return;
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(AdminConnection); await admin.OpenAsync();
        await new NpgsqlCommand($"DROP DATABASE IF EXISTS {database} WITH (FORCE); DROP ROLE IF EXISTS {runtimeRole}", admin).ExecuteNonQueryAsync();
    }

    [InvoicePostgresFact]
    public async Task Rls_and_composite_keys_block_cross_tenant_billing()
    {
        await using var db = Db(restricted: true);
        db.CustomerBillingRecipients.Add(new CustomerBillingRecipient { CustomerId = 1, Name = "Uno", Identification = "123" });
        await db.SaveChangesAsync();
        using (tenant.BeginTenantScope(2)) Assert.Empty(await db.CustomerBillingRecipients.AsNoTracking().ToListAsync());
        db.CustomerBillingRecipients.Add(new CustomerBillingRecipient { CustomerId = 2, Name = "Cruce", Identification = "456" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [InvoicePostgresFact]
    public async Task Concurrent_workers_allocate_once_and_timeout_only_reconciles()
    {
        var mock = new Mock<IElectronicInvoiceProvider>(); mock.SetupGet(x => x.Code).Returns("plemsi");
        mock.Setup(x => x.SubmitAsync(It.IsAny<ElectronicInvoiceIntegration>(), "invoice", It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("timeout"));
        mock.Setup(x => x.FindAsync(It.IsAny<ElectronicInvoiceIntegration>(), "invoice", "SETT1", It.IsAny<CancellationToken>())).ReturnsAsync((JsonElement?)null);
        await using (var db = Db())
        {
            await new ElectronicInvoiceService(db, [mock.Object], Clock()).RequestAsync(await db.Orders.SingleAsync(x => x.Id == 1), Recipient(), 1, default);
        }
        async Task Process() { await using var db = Db(); await new ElectronicInvoiceService(db, [mock.Object], Clock()).ProcessAsync(1, default); }
        await Task.WhenAll(Process(), Process());
        await Process();
        mock.Verify(x => x.SubmitAsync(It.IsAny<ElectronicInvoiceIntegration>(), "invoice", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        await using var check = Db();
        var document = await check.ElectronicInvoiceDocuments.SingleAsync();
        Assert.Equal("verifying", document.Status); Assert.Equal(1, document.Number);
        Assert.Equal(2, (await check.ElectronicInvoiceIntegrations.SingleAsync()).NextNumber);
        await Assert.ThrowsAsync<PostgresException>(() => check.Database.ExecuteSqlRawAsync("UPDATE public.\"order\" SET total=20000 WHERE id=1"));
        await Assert.ThrowsAsync<PostgresException>(() => check.Database.ExecuteSqlRawAsync("UPDATE order_detail SET order_id=2 WHERE order_id=1"));
    }

    [InvoicePostgresFact]
    public async Task Structural_http_500_is_rejected_and_existing_unknown_document_recovers_without_resubmission()
    {
        const string response = """{"code":500,"success":false,"info":"Error at line 0: Invalid data structure","data":[{"field":"multiplier_factor_numeric","reason":"Path `multiplier_factor_numeric` is required."}]}""";
        var provider = new Mock<IElectronicInvoiceProvider>(); provider.SetupGet(x => x.Code).Returns("plemsi");
        provider.Setup(x => x.SubmitAsync(It.IsAny<ElectronicInvoiceIntegration>(), "invoice", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PlemsiRequestException(System.Net.HttpStatusCode.InternalServerError, response));
        provider.Setup(x => x.FindAsync(It.IsAny<ElectronicInvoiceIntegration>(), "invoice", "SETT1", It.IsAny<CancellationToken>())).ReturnsAsync((JsonElement?)null);
        await using var db = Db(); var service = new ElectronicInvoiceService(db, [provider.Object], Clock());
        var document = await service.RequestAsync(await db.Orders.SingleAsync(x => x.Id == 1), Recipient(), 1, default);
        await service.ProcessAsync(document.Id, default);
        Assert.Equal("rejected", document.Status);
        Assert.Contains("multiplier_factor_numeric", document.LastError);
        await db.Entry(document).ReloadAsync();
        var submitted = document.SubmittedAt;
        document.Status = "verifying"; document.LastError = "Sin confirmación"; await db.SaveChangesAsync();
        await service.ProcessAsync(document.Id, default);
        Assert.Equal("rejected", document.Status); Assert.Equal(submitted, document.SubmittedAt);
        Assert.Contains("multiplier_factor_numeric", document.LastError);
        Assert.Equal(1, document.Number); Assert.Equal(2, (await db.ElectronicInvoiceIntegrations.SingleAsync()).NextNumber);
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(response), System.Text.Json.Nodes.JsonNode.Parse(document.ResponseJson!)));
        provider.Verify(x => x.FindAsync(It.IsAny<ElectronicInvoiceIntegration>(), "invoice", "SETT1", It.IsAny<CancellationToken>()), Times.Once);
        provider.Verify(x => x.SubmitAsync(It.IsAny<ElectronicInvoiceIntegration>(), "invoice", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        var user = new Mock<ICurrentUser>(); user.SetupGet(x => x.TenantId).Returns(1);
        var controller = new ElectronicInvoicingController(db, user.Object, Mock.Of<IBranchContext>(), Mock.Of<IIntegrationSecretProtector>(), [provider.Object], service, Clock(), new ConfigurationBuilder().Build());
        await controller.Retry(document.Id, Recipient(), default);
        Assert.Equal("pending", document.Status); Assert.Null(document.SubmittedAt); Assert.Equal(1, document.Number);
        Assert.Equal(2, (await db.ElectronicInvoiceIntegrations.SingleAsync()).NextNumber);
        provider.Verify(x => x.SubmitAsync(It.IsAny<ElectronicInvoiceIntegration>(), "invoice", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        provider.Setup(x => x.SubmitAsync(It.IsAny<ElectronicInvoiceIntegration>(), "invoice", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("timeout after retry"));
        await service.ProcessAsync(document.Id, default);
        Assert.Equal("verifying", document.Status); Assert.Null(document.ResponseJson);
        await service.ProcessAsync(document.Id, default);
        Assert.Equal("verifying", document.Status); Assert.Equal(1, document.Number);
        provider.Verify(x => x.SubmitAsync(It.IsAny<ElectronicInvoiceIntegration>(), "invoice", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [InvoicePostgresFact]
    public async Task Different_integrations_cannot_reuse_a_tenant_document_number()
    {
        await using var db = Db();
        var previous = new ElectronicInvoiceIntegration { Id = 2, Name = "Anterior", Prefix = "SETT", Environment = "sandbox" };
        db.ElectronicInvoiceIntegrations.Add(previous); await db.SaveChangesAsync();
        db.ElectronicInvoiceDocuments.Add(new ElectronicInvoiceDocument { OrderId = 2, IntegrationId = previous.Id,
            Environment = "sandbox", Prefix = "SETT", Number = 1, Status = "credited", RequestedById = 1 });
        await db.SaveChangesAsync();
        var provider = new Mock<IElectronicInvoiceProvider>(); provider.SetupGet(x => x.Code).Returns("plemsi");
        var service = new ElectronicInvoiceService(db, [provider.Object], Clock());
        var request = await service.RequestAsync(await db.Orders.SingleAsync(x => x.Id == 1), Recipient(), 1, default);
        await service.ProcessAsync(request.Id, default);
        Assert.Equal("rejected", request.Status); Assert.Null(request.Number);
        Assert.Equal(1, (await db.ElectronicInvoiceIntegrations.SingleAsync(x => x.Id == 1)).NextNumber);
        provider.Verify(x => x.SubmitAsync(It.IsAny<ElectronicInvoiceIntegration>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        db.ElectronicInvoiceDocuments.Add(new ElectronicInvoiceDocument { OrderId = 1, IntegrationId = 1,
            Environment = "sandbox", Prefix = "SETT", Number = 1, Kind = "invoice", Status = "cancelled", RequestedById = 1 });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [InvoicePostgresFact]
    public async Task Email_failure_does_not_reissue_and_operational_cancellation_requests_authorization()
    {
        var provider = new Mock<IElectronicInvoiceProvider>(); provider.SetupGet(x => x.Code).Returns("plemsi");
        using var accepted = JsonDocument.Parse("{\"data\":{\"cufe\":\"abc\",\"IsValid\":true}}");
        provider.Setup(x => x.SubmitAsync(It.IsAny<ElectronicInvoiceIntegration>(), "invoice", It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(accepted.RootElement.Clone());
        provider.SetupSequence(x => x.DownloadAsync(It.IsAny<ElectronicInvoiceIntegration>(), "invoice", "abc", "xml", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("file unavailable")).ReturnsAsync(InvoiceDeliveryTests.Xml());
        await using var db = Db(); var service = new ElectronicInvoiceService(db, [provider.Object], Clock());
        var order = await db.Orders.SingleAsync(x => x.Id == 1);
        var document = await service.RequestAsync(order, Recipient() with { Email = "cliente@example.com" }, 1, default);
        await service.ProcessAsync(document.Id, default);
        Assert.Equal("issued", document.Status); Assert.Equal("pending", document.EmailStatus);
        await service.ProcessAsync(document.Id, default);
        Assert.Equal("queued", document.EmailStatus); Assert.Null(document.LastError);
        Assert.Single(await db.EmailOutboxMessages.ToListAsync());
        provider.Verify(x => x.SubmitAsync(It.IsAny<ElectronicInvoiceIntegration>(), "invoice", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        await db.Database.ExecuteSqlRawAsync("UPDATE order_detail SET notes='Sin cebolla' WHERE order_id=1");
        order.Status = OrderStatus.Cancelled; await db.SaveChangesAsync();
        await service.ProcessAsync(document.Id, default);
        Assert.True(document.CreditRequested);
        Assert.Single(await db.ElectronicInvoiceDocuments.ToListAsync());
    }

    [InvoicePostgresFact]
    public async Task Dian_rejection_exposes_rules_and_corrected_retry_keeps_number_until_validated_and_delivered()
    {
        const string rejected = """{"success":true,"data":{"cude":"rejected-cufe","response":{"data":{"IsValid":"false","ErrorMessage":{"string":["Regla: FAU04, Rechazo: Base imponible inválida.","Regla: FAK24, Rechazo: DV inválido."]}}}}}""";
        using var rejection = JsonDocument.Parse(rejected);
        using var accepted = JsonDocument.Parse("""{"success":true,"data":{"cude":"accepted-cufe","response":{"data":{"IsValid":"true"}}}}""");
        var provider = new Mock<IElectronicInvoiceProvider>(); provider.SetupGet(x => x.Code).Returns("plemsi");
        provider.SetupSequence(x => x.SubmitAsync(It.IsAny<ElectronicInvoiceIntegration>(), "invoice", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rejection.RootElement.Clone()).ReturnsAsync(accepted.RootElement.Clone());
        provider.Setup(x => x.DownloadAsync(It.IsAny<ElectronicInvoiceIntegration>(), "invoice", "accepted-cufe", "xml", It.IsAny<CancellationToken>())).ReturnsAsync(InvoiceDeliveryTests.Xml(cufe: "accepted-cufe", customerId: "900507876"));
        await using var db = Db(); var service = new ElectronicInvoiceService(db, [provider.Object], Clock());
        var recipient = Recipient() with { DocumentType = 6, Identification = "900507876", Email = "cliente@example.com" };
        var document = await service.RequestAsync(await db.Orders.SingleAsync(x => x.Id == 1), recipient, 1, default);
        await service.ProcessAsync(document.Id, default);
        Assert.Equal("rejected", document.Status); Assert.Contains("FAU04", document.LastError); Assert.Contains("FAK24", document.LastError);
        Assert.Null(document.IssuedAt); Assert.Null(document.Pdf); Assert.Equal("pending", document.EmailStatus);
        document.LastError = "Mensaje genérico legacy"; await db.SaveChangesAsync();
        var user = new Mock<ICurrentUser>(); user.SetupGet(x => x.TenantId).Returns(1);
        var controller = new ElectronicInvoicingController(db, user.Object, Mock.Of<IBranchContext>(), Mock.Of<IIntegrationSecretProtector>(), [provider.Object], service, Clock(), new ConfigurationBuilder().Build());
        var result = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(await controller.Documents(1, default));
        var history = JsonSerializer.Serialize(result.Value);
        Assert.Contains("FAU04", history); Assert.Contains("FAK24", history); Assert.DoesNotContain("response_json", history);
        await controller.Retry(document.Id, recipient, default);
        Assert.Null(document.LastError); Assert.Equal(1, document.Number);
        using var payload = JsonDocument.Parse(document.PayloadJson!);
        Assert.Equal("7", payload.RootElement.GetProperty("customer").GetProperty("dv").GetString());
        Assert.Equal(10000, payload.RootElement.GetProperty("invoiceBaseTotal").GetDecimal());
        Assert.Equal(0, payload.RootElement.GetProperty("invoiceTaxExclusiveTotal").GetDecimal());
        Assert.Equal(0, payload.RootElement.GetProperty("allowanceTotal").GetDecimal());
        await service.ProcessAsync(document.Id, default);
        Assert.Equal("issued", document.Status); Assert.Equal("accepted-cufe", document.Cufe); Assert.NotNull(document.IssuedAt);
        Assert.NotNull(document.Pdf); Assert.Equal(InvoicePdf.CurrentVersion, document.PdfVersion); Assert.NotNull(document.Xml); Assert.Equal("queued", document.EmailStatus);
        Assert.Single(await db.ElectronicInvoiceDocuments.ToListAsync()); Assert.Equal(1, document.Number);
        Assert.Equal(2, (await db.ElectronicInvoiceIntegrations.SingleAsync()).NextNumber);
        provider.Verify(x => x.SubmitAsync(It.IsAny<ElectronicInvoiceIntegration>(), "invoice", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [InvoicePostgresFact]
    public async Task Rejected_invoice_retry_rebuilds_persisted_amounts_without_allocating_another_number()
    {
        var provider = new Mock<IElectronicInvoiceProvider>(); provider.SetupGet(x => x.Code).Returns("plemsi");
        using var rejection = JsonDocument.Parse("""{"IsValid":false}""");
        provider.Setup(x => x.SubmitAsync(It.IsAny<ElectronicInvoiceIntegration>(), "invoice", It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(rejection.RootElement.Clone());
        await using var db = Db(); var service = new ElectronicInvoiceService(db, [provider.Object], Clock());
        var document = await service.RequestAsync(await db.Orders.SingleAsync(x => x.Id == 1), Recipient(), 1, default);
        await service.ProcessAsync(document.Id, default); Assert.Equal("rejected", document.Status);
        var line = await db.OrderDetails.SingleAsync(x => x.OrderId == 1); line.UnitPrice = 12000; line.Subtotal = 12000;
        var order = await db.Orders.SingleAsync(x => x.Id == 1); order.Total = 12000; order.Subtotal = 12000;
        await db.SaveChangesAsync();
        var user = new Mock<ICurrentUser>(); user.SetupGet(x => x.TenantId).Returns(1);
        var controller = new ElectronicInvoicingController(db, user.Object, Mock.Of<IBranchContext>(), Mock.Of<IIntegrationSecretProtector>(), [provider.Object], service, Clock(), new ConfigurationBuilder().Build());
        await controller.Retry(document.Id, Recipient() with { Name = "Cliente corregido" }, default);
        using var payload = JsonDocument.Parse(document.PayloadJson!);
        Assert.Equal(12000, payload.RootElement.GetProperty("totalToPay").GetDecimal());
        Assert.Equal("Cliente corregido", payload.RootElement.GetProperty("customer").GetProperty("name").GetString());
        Assert.Equal("pending", document.Status); Assert.Null(document.SubmittedAt); Assert.Equal(1, document.Number);
        Assert.Equal(2, (await db.ElectronicInvoiceIntegrations.SingleAsync()).NextNumber);
    }

    [InvoicePostgresFact]
    public async Task Reservation_waits_for_delivery_and_issued_credit_unlocks_corrections()
    {
        var provider = new Mock<IElectronicInvoiceProvider>(); provider.SetupGet(x => x.Code).Returns("plemsi");
        using var accepted = JsonDocument.Parse("{\"data\":{\"cufe\":\"abc\",\"ResponseDian\":{\"IsValid\":true}}}");
        provider.Setup(x => x.SubmitAsync(It.IsAny<ElectronicInvoiceIntegration>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(accepted.RootElement.Clone());
        provider.Setup(x => x.DownloadAsync(It.IsAny<ElectronicInvoiceIntegration>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new byte[] { 1 });
        await using var db = Db(); var service = new ElectronicInvoiceService(db, [provider.Object], Clock());
        var order = await db.Orders.SingleAsync(x => x.Id == 1); order.Type = OrderType.Reservation; order.Status = OrderStatus.Taken;
        await db.SaveChangesAsync(); var document = await service.RequestAsync(order, Recipient(), 1, default);
        await service.ProcessAsync(document.Id, default); Assert.Null(document.Number);
        order.Status = OrderStatus.Delivered; await db.SaveChangesAsync();
        await service.ProcessAsync(document.Id, default); Assert.Equal("issued", document.Status);
        db.ElectronicInvoiceDocuments.Add(new ElectronicInvoiceDocument { OrderId = order.Id, IntegrationId = 1, OriginalDocumentId = document.Id, Kind = "credit", RecipientJson = document.RecipientJson, Reason = "Anulación", RequestedById = 1, AuthorizedById = 1 });
        await db.SaveChangesAsync(); await service.ProcessAsync(2, default);
        Assert.Equal("credited", document.Status);
        await db.Database.ExecuteSqlRawAsync("UPDATE public.\"order\" SET total=12000 WHERE id=1");
    }

    [InvoicePostgresFact]
    public async Task Activation_rechecks_numbering_and_prevents_used_consecutive()
    {
        await using var db = Db();
        var integration = await db.ElectronicInvoiceIntegrations.SingleAsync(); integration.IsActive = false; await db.SaveChangesAsync();
        var provider = new Mock<IElectronicInvoiceProvider>(); provider.SetupGet(x => x.Code).Returns("plemsi");
        using var resolutions = JsonDocument.Parse("""{"data":[{"resolution":"123","prefix":"SETT","from":1,"to":100,"date_to":"2030-01-01","next_number":10}]}""");
        using var remaining = JsonDocument.Parse("""{"data":{"next_number":10}}""");
        provider.Setup(x => x.ResolutionsAsync(It.IsAny<ElectronicInvoiceIntegration>(), It.IsAny<CancellationToken>())).ReturnsAsync(resolutions.RootElement.Clone());
        provider.Setup(x => x.RemainingAsync(It.IsAny<ElectronicInvoiceIntegration>(), "123", It.IsAny<CancellationToken>())).ReturnsAsync(remaining.RootElement.Clone());
        var user = new Mock<ICurrentUser>(); user.SetupGet(x => x.TenantId).Returns(1);
        var controller = new ElectronicInvoicingController(db, user.Object, Mock.Of<IBranchContext>(), Mock.Of<IIntegrationSecretProtector>(), [provider.Object], Mock.Of<IElectronicInvoiceService>(), Clock(), new ConfigurationBuilder().Build());
        await Assert.ThrowsAsync<SenorArroz.Domain.Exceptions.BusinessException>(() => controller.Activate(1, new ActivationDto(true), default));
        Assert.False(integration.IsActive);
        integration.NextNumber = 10; await db.SaveChangesAsync();
        await controller.Activate(1, new ActivationDto(true), default);
        Assert.True(integration.IsActive); Assert.NotNull(integration.ResolutionsJson);
        provider.Verify(x => x.ResolutionsAsync(It.IsAny<ElectronicInvoiceIntegration>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [InvoicePostgresFact]
    public async Task Disabled_production_never_allocates_or_transmits_pending_document()
    {
        await using var db = Db();
        var integration = await db.ElectronicInvoiceIntegrations.SingleAsync(); integration.Environment = "production"; integration.ProductionReady = true;
        db.ElectronicInvoiceDocuments.Add(new ElectronicInvoiceDocument { OrderId = 1, IntegrationId = 1, Environment = "production", RecipientJson = JsonSerializer.Serialize(Recipient()) });
        await db.SaveChangesAsync();
        var provider = new Mock<IElectronicInvoiceProvider>(); provider.SetupGet(x => x.Code).Returns("plemsi");
        await new ElectronicInvoiceService(db, [provider.Object], Clock()).ProcessAsync(1, default);
        var document = await db.ElectronicInvoiceDocuments.SingleAsync();
        Assert.Null(document.Number); Assert.Null(document.SubmittedAt); Assert.Null(document.LeaseUntil);
        Assert.Equal(1, integration.NextNumber);
        provider.Verify(x => x.SubmitAsync(It.IsAny<ElectronicInvoiceIntegration>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        integration.IsActive = false; await db.SaveChangesAsync();
        var user = new Mock<ICurrentUser>(); user.SetupGet(x => x.TenantId).Returns(1);
        var controller = new ElectronicInvoicingController(db, user.Object, Mock.Of<IBranchContext>(), Mock.Of<IIntegrationSecretProtector>(), [provider.Object], Mock.Of<IElectronicInvoiceService>(), Clock(), new ConfigurationBuilder().Build());
        await Assert.ThrowsAsync<SenorArroz.Domain.Exceptions.BusinessException>(() => controller.Activate(1, new ActivationDto(true), default));
        Assert.False(integration.IsActive);
    }

    [InvoicePostgresFact]
    public async Task Own_delivery_claims_and_enqueue_are_tenant_isolated_and_idempotent()
    {
        await using (var setup = Db())
        {
            var document = InvoiceDeliveryTests.Document(); document.OrderId = 1; document.IntegrationId = 1;
            document.Pdf = InvoicePdf.Create(document); document.PdfVersion = 1;
            setup.ElectronicInvoiceDocuments.Add(document); await setup.SaveChangesAsync();
        }
        async Task Queue() { await using var db = Db(true); await new InvoiceDeliveryService(db, Clock()).QueueAsync(1, false, default); }
        await Task.WhenAll(Queue(), Queue());
        var sends = 0;
        var http = new InvoiceDeliveryTests.Handler(async request =>
        {
            Interlocked.Increment(ref sends); await Task.Delay(80);
            return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"resend-accepted\"}") };
        });
        async Task Deliver() { await using var db = Db(true); await EmailOutboxWorker.ProcessMessageAsync(db, InvoiceDeliveryTests.Sender(db, http), Clock(), 1, default); }
        await Task.WhenAll(Deliver(), Deliver());
        await using var check = Db(true);
        Assert.Equal(1, sends);
        Assert.Equal("sent", (await check.ElectronicInvoiceDocuments.SingleAsync()).EmailStatus);
        Assert.Equal("resend-accepted", (await check.EmailOutboxMessages.SingleAsync()).ProviderMessageId);
        await new InvoiceDeliveryService(check, Clock()).QueueAsync(1, true, default);
        Assert.Single(await check.EmailOutboxMessages.ToListAsync());
        using (tenant.BeginTenantScope(2))
        {
            Assert.Empty(await check.EmailOutboxMessages.IgnoreQueryFilters().AsNoTracking().ToListAsync());
            await Assert.ThrowsAsync<PostgresException>(() => check.Database.ExecuteSqlRawAsync("""
                INSERT INTO email_outbox_message(tenant_id,message_type,related_entity_type,related_entity_id,to_emails_json,subject,body,is_html,status,attempt_count,max_attempts,metadata_json)
                VALUES(2,'electronic_invoice','electronic_invoice',1,'[]','Cruce','',false,'pending',0,5,jsonb_build_object())
                """));
        }
    }

    [InvoicePostgresFact]
    public async Task Expired_mail_idempotency_window_stops_automatic_and_manual_resend()
    {
        await using var db = Db(true);
        var document = InvoiceDeliveryTests.Document(); document.OrderId = 1; document.IntegrationId = 1;
        document.Pdf = InvoicePdf.Create(document); document.PdfVersion = 1;
        db.ElectronicInvoiceDocuments.Add(document); await db.SaveChangesAsync();
        await new InvoiceDeliveryService(db, Clock()).QueueAsync(1, false, default);
        await db.Database.ExecuteSqlRawAsync("UPDATE email_outbox_message SET first_attempted_at=NOW()-INTERVAL '25 hours', status='processing', lease_until=NOW()-INTERVAL '1 minute'");
        await using var worker = Db(true);
        var sends = 0;
        var http = new InvoiceDeliveryTests.Handler(request => { sends++; throw new InvalidOperationException("No debe enviarse"); });
        await EmailOutboxWorker.ProcessMessageAsync(worker, InvoiceDeliveryTests.Sender(worker, http), Clock(), 1, default);
        Assert.Equal(0, sends); Assert.Equal("verifying", (await worker.EmailOutboxMessages.SingleAsync()).Status);
        await Assert.ThrowsAsync<SenorArroz.Domain.Exceptions.BusinessException>(() => new InvoiceDeliveryService(worker, Clock()).QueueAsync(1, true, default));
    }

    [InvoicePostgresFact]
    public async Task Historical_provider_delivery_does_not_send_again_when_preparing_a_new_copy()
    {
        await using var db = Db(true);
        var document = InvoiceDeliveryTests.Document(); document.OrderId = 1; document.IntegrationId = 1;
        document.Pdf = [1, 2, 3]; document.EmailStatus = "accepted"; document.EmailChannel = "plemsi";
        db.ElectronicInvoiceDocuments.Add(document); await db.SaveChangesAsync();
        var xml = document.Xml;
        var delivery = new InvoiceDeliveryService(db, Clock());
        await delivery.PreparePdfAsync(1, default); await delivery.QueueAsync(1, false, default);
        var copy = document.Pdf;
        await delivery.PreparePdfAsync(1, default);
        Assert.Equal(copy, document.Pdf); Assert.Equal(new byte[] { 1, 2, 3 }, document.ProviderPdf);
        Assert.Equal(xml, document.Xml); Assert.Equal("accepted", document.EmailStatus);
        Assert.Empty(await db.EmailOutboxMessages.ToListAsync());
    }

    [InvoicePostgresFact]
    public async Task Lost_email_acceptance_retries_the_same_message_key_and_attachments()
    {
        await using var db = Db(true);
        var document = InvoiceDeliveryTests.Document(); document.OrderId = 1; document.IntegrationId = 1;
        document.Pdf = InvoicePdf.Create(document); document.PdfVersion = 1;
        db.ElectronicInvoiceDocuments.Add(document); await db.SaveChangesAsync();
        await new InvoiceDeliveryService(db, Clock()).QueueAsync(1, false, default);
        var requests = new List<(string Key, string Body)>();
        var http = new InvoiceDeliveryTests.Handler(async request =>
        {
            requests.Add((request.Headers.GetValues("Idempotency-Key").Single(), await request.Content!.ReadAsStringAsync()));
            if (requests.Count == 1) throw new TaskCanceledException("Respuesta perdida después de aceptar");
            return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"same-email\"}") };
        });
        var sender = InvoiceDeliveryTests.Sender(db, http);
        await EmailOutboxWorker.ProcessMessageAsync(db, sender, Clock(), 1, default);
        Assert.Equal("queued", document.EmailStatus);
        await db.Database.ExecuteSqlRawAsync("UPDATE email_outbox_message SET next_attempt_at=NOW()-INTERVAL '1 minute'");
        await EmailOutboxWorker.ProcessMessageAsync(db, sender, Clock(), 1, default);
        Assert.Equal(2, requests.Count); Assert.Equal(requests[0], requests[1]);
        Assert.Equal("sent", document.EmailStatus); Assert.Single(await db.EmailOutboxMessages.ToListAsync());
        Assert.Single(await db.ElectronicInvoiceDocuments.ToListAsync());
    }

    [InvoicePostgresFact]
    public async Task Branding_is_immutable_tenant_owned_and_documents_keep_the_selected_version()
    {
        await using var db = Db(true);
        using (tenant.BeginTenantScope(2))
        {
            db.ElectronicInvoiceBrandings.Add(new ElectronicInvoiceBranding { Id = 20, DisplayName = "Negocio ajeno", Logo = InvoiceDeliveryTests.Logo });
            await db.SaveChangesAsync();
        }
        Assert.DoesNotContain(await db.ElectronicInvoiceBrandings.IgnoreQueryFilters().AsNoTracking().ToListAsync(), x => x.Id == 20);
        var document = InvoiceDeliveryTests.Document(); document.OrderId = 1; document.IntegrationId = 1; document.BrandingId = 1;
        db.ElectronicInvoiceDocuments.Add(document); await db.SaveChangesAsync();
        db.ElectronicInvoiceBrandings.Add(new ElectronicInvoiceBranding { Id = 2, DisplayName = "Nueva identidad", Logo = InvoiceDeliveryTests.Logo });
        await db.SaveChangesAsync();
        await new InvoiceDeliveryService(db, Clock()).PreparePdfAsync(document.Id, default);
        Assert.Equal(1, document.BrandingId); Assert.Equal(InvoicePdf.CurrentVersion, document.PdfVersion);
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE electronic_invoice_branding SET display_name='Cambio' WHERE id=1"));
        await using var cross = Db(true);
        var invalid = await cross.ElectronicInvoiceDocuments.SingleAsync(); invalid.BrandingId = 20;
        await Assert.ThrowsAsync<DbUpdateException>(() => cross.SaveChangesAsync());
    }

    private static BillingRecipientDto Recipient() => new() { DocumentType = 3, Identification = "12345", Name = "Cliente" };
    private ElectronicInvoicingController DirectoryController(ApplicationDbContext db, IElectronicInvoiceProvider? provider = null)
    {
        var user = new Mock<ICurrentUser>(); user.SetupGet(x => x.TenantId).Returns(tenant.TenantId);
        return new(db, user.Object, Mock.Of<IBranchContext>(), Mock.Of<IIntegrationSecretProtector>(), provider is null ? [] : [provider], Mock.Of<IElectronicInvoiceService>(), Clock(), new ConfigurationBuilder().Build());
    }
    private static object ResponseData(Microsoft.AspNetCore.Mvc.IActionResult result) =>
        Assert.IsType<SenorArroz.Shared.Models.ApiResponse<object>>(Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result).Value).Data!;

    [InvoicePostgresFact]
    public async Task First_lookup_uses_Dian_and_saved_profiles_are_reused_for_other_customers_without_a_provider()
    {
        await using var db = Db(true);
        db.Customers.Add(new Customer { Id = 3, BranchId = 1, Name = "Otro empleado" }); await db.SaveChangesAsync();
        var provider = new Mock<IElectronicInvoiceProvider>(); provider.SetupGet(x => x.Code).Returns("plemsi");
        provider.Setup(x => x.LookupRecipientAsync(It.IsAny<ElectronicInvoiceIntegration>(), 6, "900507876", It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.SerializeToElement(new { customerName = "METTAGRUP S.A.S.", customerEmail = "factura@example.com" }));
        var controller = DirectoryController(db, provider.Object);
        Assert.Equal("dian", JsonSerializer.SerializeToElement(ResponseData(await controller.Lookup(6, "900507876-7", default))).GetProperty("source").GetString());
        Assert.Empty(await db.BillingRecipientProfiles.ToListAsync());
        var saved = Assert.IsType<BillingRecipientDto>(ResponseData(await controller.SaveRecipient(1, new BillingRecipientDto
            { DocumentType = 6, Identification = "900507876-7", Name = "METTAGRUP S.A.S.", TradeName = "Metta", Email = "factura@example.com", Address = "Sede fiscal" }, default)));
        await controller.SaveRecipient(3, saved with { Id = null, IsDefault = true }, default);
        var integration = await db.ElectronicInvoiceIntegrations.SingleAsync(); integration.IsActive = false; await db.SaveChangesAsync();
        var cached = JsonSerializer.SerializeToElement(ResponseData(await controller.Lookup(6, "900507876", default)));
        Assert.Equal("directory", cached.GetProperty("source").GetString());
        Assert.Single(await db.BillingRecipientProfiles.ToListAsync());
        Assert.Equal(2, await db.CustomerBillingRecipients.CountAsync(x => x.ProfileId == saved.ProfileId));
        Assert.Empty(await db.ElectronicInvoiceDocuments.ToListAsync());
        provider.Verify(x => x.LookupRecipientAsync(It.IsAny<ElectronicInvoiceIntegration>(), 6, "900507876", It.IsAny<CancellationToken>()), Times.Once);
        provider.Verify(x => x.SubmitAsync(It.IsAny<ElectronicInvoiceIntegration>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [InvoicePostgresFact]
    public async Task Shared_edits_require_confirmation_and_version_and_preserve_issued_snapshots()
    {
        await using var db = Db(true); var controller = DirectoryController(db);
        var saved = Assert.IsType<BillingRecipientDto>(ResponseData(await controller.SaveRecipient(1, Recipient() with { Email = "original@example.com" }, default)));
        await Assert.ThrowsAsync<SenorArroz.Domain.Exceptions.BusinessException>(() => controller.SaveDirectoryRecipient(Recipient() with { Email = "otro@example.com" }, default));
        var document = InvoiceDeliveryTests.Document(); document.OrderId = 1; document.IntegrationId = 1; document.RecipientJson = JsonSerializer.Serialize(saved);
        db.ElectronicInvoiceDocuments.Add(document); await db.SaveChangesAsync(); var original = document.RecipientJson;
        await Assert.ThrowsAsync<SenorArroz.Domain.Exceptions.BusinessException>(() => controller.SaveDirectoryRecipient(saved with { Email = "nuevo@example.com" }, default));
        var updated = Assert.IsType<BillingRecipientDto>(ResponseData(await controller.SaveDirectoryRecipient(saved with { Email = "nuevo@example.com", ConfirmSharedUpdate = true }, default)));
        Assert.NotEqual(saved.ProfileVersion, updated.ProfileVersion);
        var service = new ElectronicInvoiceService(db, [], Clock());
        var secondOrder = await db.Orders.SingleAsync(x => x.Id == 2);
        var stale = await Assert.ThrowsAsync<SenorArroz.Domain.Exceptions.BusinessException>(() => service.RequestAsync(secondOrder, saved, 1, default));
        Assert.Contains("cambiaron", stale.Message);
        await Assert.ThrowsAsync<SenorArroz.Domain.Exceptions.BusinessException>(() => controller.SaveDirectoryRecipient(saved with { Email = "obsoleto@example.com", ConfirmSharedUpdate = true }, default));
        var linked = (IEnumerable<BillingRecipientDto>)ResponseData(await controller.Recipients(1, default));
        Assert.Equal("nuevo@example.com", Assert.Single(linked).Email);
        Assert.Equal(original, (await db.ElectronicInvoiceDocuments.SingleAsync()).RecipientJson);
    }

    [InvoicePostgresFact]
    public async Task Concurrent_creates_deduplicate_profiles_and_local_search_is_paginated_and_tenant_isolated()
    {
        async Task Save() { await using var db = Db(true); await DirectoryController(db).SaveDirectoryRecipient(Recipient() with { TradeName = "Empresa compartida" }, default); }
        await Task.WhenAll(Save(), Save());
        await using var db = Db(true);
        Assert.Single(await db.BillingRecipientProfiles.ToListAsync());
        using (tenant.BeginTenantScope(2))
        {
            db.BillingRecipientProfiles.Add(new BillingRecipientProfile { Id = 90, DocumentType = 3, Identification = "12345", Name = "Ajeno", TradeName = "Empresa compartida" }); await db.SaveChangesAsync();
        }
        Assert.DoesNotContain(await db.BillingRecipientProfiles.IgnoreQueryFilters().ToListAsync(), x => x.Id == 90);
        var result = Assert.IsType<SenorArroz.Shared.Models.PagedResult<BillingRecipientDto>>(ResponseData(await DirectoryController(db).SearchRecipients("compartida", 1, default)));
        Assert.Equal(1, result.TotalCount); Assert.Equal(20, result.PageSize); Assert.Single(result.Items);
        var missing = Assert.IsType<SenorArroz.Shared.Models.PagedResult<BillingRecipientDto>>(ResponseData(await DirectoryController(db).SearchRecipients("%", 1, default)));
        Assert.Empty(missing.Items);
        await Assert.ThrowsAsync<SenorArroz.Domain.Exceptions.NotFoundException>(() => DirectoryController(db).SaveDirectoryRecipient(Recipient() with { ProfileId = 90 }, default));
        db.ChangeTracker.Clear(); db.CustomerBillingRecipients.Add(new CustomerBillingRecipient { CustomerId = 1, ProfileId = 90, Name = "Cruce", Identification = "111" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [InvoicePostgresFact]
    public async Task Customer_search_by_company_alias_and_nit_keeps_customer_and_branch_scope()
    {
        await using var db = Db(true);
        db.Customers.Add(new Customer { Id = 3, BranchId = 1, Name = "Empleado distinto" }); await db.SaveChangesAsync();
        var controller = DirectoryController(db);
        var saved = Assert.IsType<BillingRecipientDto>(ResponseData(await controller.SaveRecipient(1, new BillingRecipientDto { DocumentType = 6, Identification = "900507876", Name = "METTAGRUP S.A.S.", TradeName = "Empresa Metta" }, default)));
        await controller.SaveRecipient(3, saved with { Id = null }, default);
        var repository = new SenorArroz.Infrastructure.Repositories.CustomerRepository(db, tenant);
        foreach (var term in new[] { "metta", "900507876", "METTAGRUP" })
        {
            var results = await repository.GetPagedAsync(1, search: term);
            Assert.Equal(new[] { 1, 3 }, results.Items.Select(x => x.Id).Order().ToArray());
            Assert.All(results.Items, c => Assert.Single(c.BillingRecipients));
        }
        Assert.Empty((await repository.GetPagedAsync(2, search: "metta")).Items);
        Assert.Empty((await repository.GetPagedAsync(1, search: "no existe")).Items);
    }

    [InvoicePostgresFact]
    public async Task Directory_migration_keeps_legacy_ids_and_flags_conflicting_contacts_without_rewriting_invoices()
    {
        await using var db = Db();
        db.Customers.Add(new Customer { Id = 3, BranchId = 1, Name = "Otro" }); await db.SaveChangesAsync();
        var first = new CustomerBillingRecipient { CustomerId = 1, DocumentType = 6, Identification = "900507876", Name = "METTAGRUP S.A.S.", Email = "uno@example.com", IsDefault = true };
        var second = new CustomerBillingRecipient { CustomerId = 3, DocumentType = 6, Identification = "900507876-7", Name = "METTAGRUP S.A.S.", Email = "dos@example.com", IsDefault = true };
        db.CustomerBillingRecipients.AddRange(first, second); await db.SaveChangesAsync();
        using (tenant.BeginSystemScope())
        {
            await db.Database.OpenConnectionAsync(); await using var command = db.Database.GetDbConnection().CreateCommand(); command.CommandText = Script();
            await command.ExecuteNonQueryAsync(); await command.ExecuteNonQueryAsync();
        }
        db.ChangeTracker.Clear();
        var profile = await db.BillingRecipientProfiles.SingleAsync(); Assert.True(profile.NeedsReview); Assert.Null(profile.Email);
        var links = await db.CustomerBillingRecipients.OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(new[] { first.Id, second.Id }, links.Select(x => x.Id).ToArray());
        Assert.All(links, x => Assert.Equal(profile.Id, x.ProfileId)); Assert.Equal("uno@example.com", links[0].Email); Assert.Equal("dos@example.com", links[1].Email);
    }
    private static IClock Clock() { var clock = new Mock<IClock>(); clock.SetupGet(x => x.UtcNow).Returns(() => DateTime.UtcNow); return clock.Object; }
}

public sealed class InvoicePostgresFactAttribute : FactAttribute
{
    public InvoicePostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("INVOICE_TEST_POSTGRES"))) Skip = "Configura una conexión PostgreSQL de pruebas con permiso para crear una base aislada.";
    }
}
