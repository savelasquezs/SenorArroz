using System.Text.Json;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.Queries;
using SenorArroz.Application.Mappings;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Repositories;
using Testcontainers.PostgreSql;

namespace SenorArroz.Tests;

/// <summary>Kitchen list returns complete lines for the scoped page without N detail endpoints.</summary>
public sealed class KitchenListDetailsTests
{
    private static readonly DateTime Now = new(2026, 10, 15, 16, 0, 0, DateTimeKind.Utc);

    private sealed record Actor(string Role, int BranchId) : ICurrentUser
    {
        public int Id => 17;
        public int TenantId => 1;
        public bool IsAuthenticated => true;
    }

    private static async Task<(int FirstBranch, int[] OrderIds)> SeedAsync(ApplicationDbContext context)
    {
        context.Tenants.Add(new Tenant { Id = 1, Name = "Kitchen", Slug = "kitchen-batch" });
        await context.SaveChangesAsync();
        var first = new Branch { Name = "Santander", Address = "A", Phone1 = "1" };
        var second = new Branch { Name = "Manrique", Address = "B", Phone1 = "2" };
        context.Branches.AddRange(first, second);
        await context.SaveChangesAsync();

        var user = new User
        {
            Name = "Kitchen user", Email = $"kitchen_{Guid.NewGuid():N}@example.com",
            PasswordHash = "test-only", Role = UserRole.Kitchen, BranchId = first.Id,
            Active = true, CreatedAt = Now
        };
        context.Users.Add(user);
        var category = new ProductCategory { BranchId = first.Id, Name = "Arroces" };
        context.ProductCategories.Add(category);
        await context.SaveChangesAsync();
        var product = new Product { CategoryId = category.Id, Name = "Arroz Paisa", Price = 18000 };
        context.Products.Add(product);
        await context.SaveChangesAsync();

        foreach (var branch in new[] { first.Id, first.Id, second.Id })
        {
            context.Orders.Add(new Order
            {
                BranchId = branch, TakenById = user.Id,
                Type = OrderType.Delivery, Status = OrderStatus.Taken,
                StatusTimes = "{}", Subtotal = 18000, Total = 18000,
                CreatedAt = Now, UpdatedAt = Now
            });
        }
        await context.SaveChangesAsync();
        var ids = await context.Orders.AsNoTracking().OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync();
        // Multiple notes/quantities in one order, second order has no product lines.
        context.OrderDetails.AddRange(
            new OrderDetail { OrderId = ids[0], ProductId = product.Id, Quantity = 2,
                UnitPrice = 18000, Notes = "Sin cebolla" },
            new OrderDetail { OrderId = ids[0], ProductId = product.Id, Quantity = 1,
                UnitPrice = 18000, Notes = "Poco picante" },
            new OrderDetail { OrderId = ids[2], ProductId = product.Id, Quantity = 3,
                UnitPrice = 18000, Notes = "Other branch" });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return (first.Id, ids);
    }

    private static GetOrdersHandler Handler(ApplicationDbContext context, int branchId) =>
        new(new OrderRepository(context, new FakeClock(Now)),
            new MapperConfiguration(config => config.AddProfile<OrderMappingProfile>(),
                NullLoggerFactory.Instance).CreateMapper(),
            new Actor(Roles.Kitchen, branchId), context);

    private static GetOrdersQuery Query(bool kitchen, int page, int pageSize) => new()
    {
        ForKitchen = kitchen, Page = page, PageSize = pageSize,
        SortBy = "id", SortOrder = "asc",
        FromDate = new DateTime(2026, 10, 15, 0, 0, 0),
        ToDate = new DateTime(2026, 10, 15, 23, 59, 59)
    };

    [Fact]
    public async Task Kitchen_list_lines_only_for_authorized_page_and_preserves_empty_lines()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        await using var db = new ApplicationDbContext(options,
            currentTenant: TestTenantContext.Default,
            tenantExecutionContext: TestTenantContext.Default);
        var (branch, ids) = await SeedAsync(db);
        var handler = Handler(db, branch);

        var first = await handler.Handle(Query(true, 1, 1), CancellationToken.None);
        Assert.Equal(2, first.TotalCount); // Other branch is never returned
        Assert.Equal(ids[0], Assert.Single(first.Items).Id);
        var details = Assert.Single(first.Items).KitchenDetails;
        Assert.NotNull(details);
        Assert.Equal(2, details.Count);
        Assert.Equal(new[] { 2, 1 }, details.Select(d => d.Quantity));
        Assert.Equal("Sin cebolla", details[0].Notes);
        Assert.Equal("Arroces", details[0].ProductCategoryName);

        var second = await handler.Handle(Query(true, 2, 1), CancellationToken.None);
        Assert.Equal(ids[1], Assert.Single(second.Items).Id);
        Assert.Empty(second.Items[0].KitchenDetails!);

        var regular = await handler.Handle(Query(false, 1, 1), CancellationToken.None);
        Assert.Null(regular.Items[0].KitchenDetails);
        var json = JsonSerializer.Serialize(regular.Items[0],
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("kitchenDetails", json);
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Kitchen_lines_are_returned_with_postgresql_relations()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).Options;
        await using var db = new ApplicationDbContext(options,
            currentTenant: TestTenantContext.Default,
            tenantExecutionContext: TestTenantContext.Default);
        await db.Database.EnsureCreatedAsync();
        var (branch, ids) = await SeedAsync(db);
        var result = await Handler(db, branch).Handle(Query(true, 1, 10), CancellationToken.None);
        Assert.Equal(new[] { ids[0], ids[1] }, result.Items.Select(o => o.Id));
        Assert.Equal(2, result.Items[0].KitchenDetails!.Count);
        Assert.Empty(result.Items[1].KitchenDetails!);
    }
}
