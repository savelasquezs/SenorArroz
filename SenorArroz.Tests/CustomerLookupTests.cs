using System.Data.Common;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using SenorArroz.API.Controllers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Customers.DTOs;
using SenorArroz.Application.Features.Customers.Queries;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Repositories;
using Testcontainers.PostgreSql;
using Xunit.Abstractions;

namespace SenorArroz.Tests;

public sealed class CustomerLookupTests(ITestOutputHelper output)
{
    private static ICurrentUser User(int tenant = 1, int branch = 1, string role = "Cashier") =>
        Mock.Of<ICurrentUser>(u => u.Id == 7 && u.TenantId == tenant && u.BranchId == branch
            && u.Role == role && u.IsAuthenticated == true);

    private static ApplicationDbContext MemoryDb(TestTenantContext tenant) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        currentTenant: tenant, tenantExecutionContext: tenant);

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("3")]
    public async Task Short_input_never_queries_the_database(string search)
    {
        // No DbSet is configured: any attempted database access would fail.
        var handler = new GetCustomerSuggestionsHandler(Mock.Of<IApplicationDbContext>(), User());
        var result = await handler.Handle(new() { Search = search, BranchId = 1 }, default);
        Assert.Empty(result.Items);
        Assert.False(result.HasMore);
    }

    [Fact]
    public async Task Validates_bounds_identity_and_cancellation_before_reading()
    {
        var db = Mock.Of<IApplicationDbContext>();
        var handler = new GetCustomerSuggestionsHandler(db, User());
        await Assert.ThrowsAsync<BusinessException>(() => handler.Handle(new() { Search = new string('a', 121) }, default));
        await Assert.ThrowsAsync<BusinessException>(() => handler.Handle(new() { Search = "test", Page = 0 }, default));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            handler.Handle(new() { Search = "test" }, new CancellationToken(true)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new GetCustomerSuggestionsHandler(db, User(tenant: 0)).Handle(new() { Search = "test" }, default));
    }

    [Fact]
    public async Task Controller_preserves_branch_authorization_and_passes_request_cancellation()
    {
        using var source = new CancellationTokenSource();
        var branch = new Mock<IBranchContext>(MockBehavior.Strict);
        branch.Setup(b => b.RequireBranch(null)).Returns(2);
        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        mediator.Setup(m => m.Send(It.Is<GetCustomerSuggestionsQuery>(q => q.BranchId == 2 && q.Search == "ana"), source.Token))
            .ReturnsAsync(new CustomerSuggestionPageDto([], 1, false));
        var controller = new CustomerLookupController(mediator.Object, branch.Object);
        Assert.IsType<OkObjectResult>((await controller.Search(new() { Search = "ana" }, source.Token)).Result);
        branch.VerifyAll(); mediator.VerifyAll();

        branch.Setup(b => b.RequireBranch(99)).Throws(new UnauthorizedAccessException());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => controller.Search(new() { Search = "ana", BranchId = 99 }, source.Token));
        mediator.Verify(m => m.Send(It.IsAny<GetCustomerSuggestionsQuery>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Selection_reuses_full_customer_handler_and_propagates_token()
    {
        using var source = new CancellationTokenSource();
        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        mediator.Setup(m => m.Send(It.Is<GetCustomerByIdQuery>(q => q.Id == 5), source.Token))
            .ReturnsAsync(new CustomerDto { Id = 5, Active = true, Name = "Selected" });
        mediator.Setup(m => m.Send(It.Is<GetCustomerByIdQuery>(q => q.Id == 6), source.Token))
            .ReturnsAsync(new CustomerDto { Id = 6, Active = false });
        var controller = new CustomerLookupController(mediator.Object, Mock.Of<IBranchContext>());
        Assert.IsType<OkObjectResult>((await controller.Select(5, source.Token)).Result);
        Assert.IsType<NotFoundObjectResult>((await controller.Select(6, source.Token)).Result);
        mediator.VerifyAll();
    }

    private sealed class Reads : DbCommandInterceptor
    {
        public List<string> Sql { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Sql.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task PostgreSql_lookup_is_bounded_tenant_scoped_and_equivalent_to_existing_search()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();
        var tenant = new TestTenantContext(1);
        var reads = new Reads();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(postgres.GetConnectionString())
            .AddInterceptors(reads).Options;
        await using var db = new ApplicationDbContext(options, currentTenant: tenant, tenantExecutionContext: tenant);
        await db.Database.EnsureCreatedAsync();
        using (tenant.BeginSystemScope())
        {
            db.Tenants.AddRange(new Tenant { Id = 1, Name = "Lookup", Slug = "lookup" }, new Tenant { Id = 2, Name = "Other", Slug = "lookup-other" });
            db.Branches.AddRange(Enumerable.Range(1, 3).Select(id => new Branch
            { Id = id, TenantId = id == 3 ? 2 : 1, Name = $"Branch {id}", Address = "Synthetic", Phone1 = id.ToString() }));
            Customer Make(int id, int branch, string name, string? phone = null, bool active = true) => new()
            {
                Id = id, TenantId = branch == 3 ? 2 : 1, BranchId = branch, Name = name, Active = active, Phone1 = phone,
                Phones = phone is null ? [] : [new CustomerPhone { TenantId = branch == 3 ? 2 : 1, PhoneNormalized = phone, Active = true, IsPrimary = true }]
            };
            db.Customers.AddRange(
                Make(1, 2, "Z Exact mobile", "3001111111"),
                Make(2, 1, "Same Name", "3011111111"),
                Make(3, 2, "Same Name", "3021111111"),
                Make(4, 3, "Same Name", "3001111111"),
                Make(5, 1, "Inactive", "3031111111", false));
            var username = Make(6, 1, "User account"); username.WhatsAppUsername = "operador"; db.Customers.Add(username);
            db.Customers.AddRange(Enumerable.Range(20, 25).Select(id => Make(id, 1, $"Match {id:00}")));
            db.Customers.Add(Make(50, 1, "Match"));
            db.BillingRecipientProfiles.AddRange(
                new BillingRecipientProfile { Id = 1, TenantId = 1, Identification = "900123456", Name = "Empresa prueba", TradeName = "Compañía prueba" },
                new BillingRecipientProfile { Id = 2, TenantId = 1, Identification = "3001111111", Name = "Empresa teléfono" },
                new BillingRecipientProfile { Id = 3, TenantId = 2, Identification = "900123456", Name = "Empresa prueba" });
            db.CustomerBillingRecipients.AddRange(
                new CustomerBillingRecipient { Id = 1, TenantId = 1, CustomerId = 2, ProfileId = 1, Identification = "900123456", Name = "Empresa prueba" },
                new CustomerBillingRecipient { Id = 2, TenantId = 1, CustomerId = 3, ProfileId = 1, Identification = "900123456", Name = "Empresa prueba" },
                new CustomerBillingRecipient { Id = 3, TenantId = 1, CustomerId = 2, ProfileId = 2, Identification = "3001111111", Name = "Empresa teléfono" },
                new CustomerBillingRecipient { Id = 4, TenantId = 2, CustomerId = 4, ProfileId = 3, Identification = "900123456", Name = "Empresa prueba" });
            await db.SaveChangesAsync();
        }
        db.ChangeTracker.Clear();
        var handler = new GetCustomerSuggestionsHandler(db, User());
        Task<CustomerSuggestionPageDto> Search(string term, int page = 1) => handler.Handle(new() { Search = term, BranchId = 1, Page = page }, default);

        reads.Sql.Clear();
        var first = await Search("Match");
        Assert.Equal(10, first.Items.Count); Assert.True(first.HasMore); Assert.Equal(50, first.Items[0].Id);
        Assert.Equal(2, reads.Sql.Count);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.DoesNotContain(reads.Sql, sql => sql.Contains("COUNT(", StringComparison.OrdinalIgnoreCase));
        var second = await Search("Match", 2); var third = await Search("Match", 3);
        Assert.Equal(10, second.Items.Count); Assert.True(second.HasMore);
        Assert.Equal(6, third.Items.Count); Assert.False(third.HasMore);
        Assert.Equal(26, first.Items.Concat(second.Items).Concat(third.Items).Select(x => x.Id).Distinct().Count());

        var repository = new CustomerRepository(db, tenant);
        foreach (var term in new[] { "Same", "301", "@operador", "Empresa", "900123456", "+57 300 111 1111" })
        {
            var legacy = await repository.GetPagedAsync(1, search: term, active: true, pageSize: 100);
            var light = await Search(term);
            Assert.Equal(legacy.Items.Select(x => x.Id).OrderBy(x => x), light.Items.Select(x => x.Id).OrderBy(x => x));
        }
        Assert.Equal(new[] { 2 }, (await Search("Same")).Items.Select(x => x.Id));
        Assert.Equal(new[] { 1, 2 }, (await Search("3001111111")).Items.Select(x => x.Id));
        Assert.Contains("Compañía prueba", (await Search("Empresa")).Items.Single().BillingCompanies);
        Assert.Empty((await Search("Inactive")).Items);
        // Non-superadmin cannot widen the query by constructing a different branch in the handler.
        Assert.Equal(new[] { 2 }, (await handler.Handle(new() { Search = "Same", BranchId = 2 }, default)).Items.Select(x => x.Id));
        var admin = new GetCustomerSuggestionsHandler(db, User(role: "Superadmin"));
        Assert.Equal(new[] { 3 }, (await admin.Handle(new() { Search = "Same", BranchId = 2 }, default)).Items.Select(x => x.Id));
        using (tenant.BeginTenantScope(2))
        {
            var other = new GetCustomerSuggestionsHandler(db, User(tenant: 2, branch: 3));
            Assert.Equal(new[] { 4 }, (await other.Handle(new() { Search = "3001111111", BranchId = 3 }, default)).Items.Select(x => x.Id));
        }
        reads.Sql.Clear(); Assert.Empty((await Search("zzzz-no-customer")).Items); Assert.Single(reads.Sql);
        reads.Sql.Clear(); await Search("3"); Assert.Empty(reads.Sql);
        output.WriteLine("Isolated PostgreSQL: populated suggestion page = 2 SELECTs, 10 rows + hasMore; no COUNT, no tracked entities. Empty page = 1 SELECT; short input = 0. Existing search identities and tenant/branch rules preserved.");
        // EnsureCreated tests EF filters, not production RLS installation; the existing RLS suite remains required.
    }
}
