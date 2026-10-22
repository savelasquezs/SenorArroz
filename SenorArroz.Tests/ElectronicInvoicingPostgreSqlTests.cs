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
            if (File.Exists(path)) return File.ReadAllText(path);
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
        provider.Setup(x => x.DownloadAsync(It.IsAny<ElectronicInvoiceIntegration>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new byte[] { 1 });
        provider.SetupSequence(x => x.SendEmailAsync(It.IsAny<ElectronicInvoiceIntegration>(), "abc", "cliente@example.com", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("mail unavailable")).Returns(Task.CompletedTask);
        await using var db = Db(); var service = new ElectronicInvoiceService(db, [provider.Object], Clock());
        var order = await db.Orders.SingleAsync(x => x.Id == 1);
        var document = await service.RequestAsync(order, Recipient() with { Email = "cliente@example.com" }, 1, default);
        await service.ProcessAsync(document.Id, default);
        Assert.Equal("issued", document.Status); Assert.Equal("pending", document.EmailStatus);
        await service.ProcessAsync(document.Id, default);
        Assert.Equal("accepted", document.EmailStatus); Assert.Null(document.LastError);
        provider.Verify(x => x.SubmitAsync(It.IsAny<ElectronicInvoiceIntegration>(), "invoice", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        await db.Database.ExecuteSqlRawAsync("UPDATE order_detail SET notes='Sin cebolla' WHERE order_id=1");
        order.Status = OrderStatus.Cancelled; await db.SaveChangesAsync();
        await service.ProcessAsync(document.Id, default);
        Assert.True(document.CreditRequested);
        Assert.Single(await db.ElectronicInvoiceDocuments.ToListAsync());
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

    private static BillingRecipientDto Recipient() => new() { DocumentType = 3, Identification = "12345", Name = "Cliente" };
    private static IClock Clock() { var clock = new Mock<IClock>(); clock.SetupGet(x => x.UtcNow).Returns(() => DateTime.UtcNow); return clock.Object; }
}

public sealed class InvoicePostgresFactAttribute : FactAttribute
{
    public InvoicePostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("INVOICE_TEST_POSTGRES"))) Skip = "Configura una conexión PostgreSQL de pruebas con permiso para crear una base aislada.";
    }
}
