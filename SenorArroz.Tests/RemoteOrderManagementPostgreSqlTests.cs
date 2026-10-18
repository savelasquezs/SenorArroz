using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using SenorArroz.Application.Common.Behaviors;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.Commands;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Repositories;
using Testcontainers.PostgreSql;

namespace SenorArroz.Tests;

/// <summary>Real row locks, immutable provenance trigger and transaction ownership; no production database.</summary>
public sealed class RemoteOrderManagementPostgreSqlTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private DbContextOptions<ApplicationDbContext> options = null!;
    private sealed record Actor : ICurrentUser
    {
        public int Id => 70;
        public int TenantId => 1;
        public int BranchId => 1;
        public string Role => "Cashier";
        public bool IsAuthenticated => true;
    }
    private sealed class Clock : IClock { public DateTime UtcNow => new(2026, 10, 12, 17, 0, 0, DateTimeKind.Utc); }
    private sealed class Snapshot(long version) : IOrderMutationContext
    {
        public (int OrderId, long Version)? Expected => (10, version);
        public void Complete(int orderId, long newVersion) { }
    }
    private ApplicationDbContext Db() => new(options, currentTenant: TestTenantContext.Default, tenantExecutionContext: TestTenantContext.Default);
    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(postgres.GetConnectionString()).Options;
        await using var db = Db();
        await db.Database.EnsureCreatedAsync();
        var script = FindScript();
        // Running twice also validates additive/idempotent deployment instructions.
        await db.Database.ExecuteSqlRawAsync(await File.ReadAllTextAsync(script));
        await db.Database.ExecuteSqlRawAsync(await File.ReadAllTextAsync(script));
        db.Tenants.Add(new Tenant { Id = 1, Name = "Remote test", Slug = "remote-test" });
        db.Branches.AddRange(new Branch { Id = 1, Name = "Origen", Address = "A", Phone1 = "3000000000" },
            new Branch { Id = 2, Name = "Destino", Address = "B", Phone1 = "3000000001" });
        db.Users.Add(new User { Id = 70, BranchId = 1, Name = "Caja", Email = "remote@test.local", PasswordHash = "x", Role = UserRole.Cashier });
        db.Orders.Add(new Order { Id = 10, BranchId = 2, OriginBranchId = 1, TakenById = 70,
            Type = OrderType.Onsite, Status = OrderStatus.Taken, StatusTimes = "{}", ManagementVersion = 1 });
        await db.SaveChangesAsync();
    }
    public async Task DisposeAsync() => await postgres.DisposeAsync();
    private static string FindScript()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "SenorArroz.Infrastructure", "Scripts", "add_remote_order_management.sql");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("Remote-order schema script not found");
    }
    private static OrderMutationBehavior<T, OrderDto> Pipeline<T>(ApplicationDbContext db, long version = 1) where T : notnull =>
        new(db, new Actor(), new TestBranchContext(1), Mock.Of<IOrderNotificationService>(),
            NullLogger<OrderMutationBehavior<T, OrderDto>>.Instance, new Snapshot(version), new Clock());
    private static IMapper Mapper()
    {
        var mapper = new Mock<IMapper>();
        mapper.Setup(m => m.Map(It.IsAny<UpdateOrderDto>(), It.IsAny<Order>()))
            .Returns((UpdateOrderDto input, Order target) => { target.Notes = input.Notes; return target; });
        mapper.Setup(m => m.Map<OrderDto>(It.IsAny<object>())).Returns((object source) =>
        {
            var order = (Order)source;
            return new OrderDto { Id = order.Id, BranchId = order.BranchId, OriginBranchId = order.OriginBranchId,
                ManagementVersion = order.ManagementVersion, Status = order.Status };
        });
        return mapper.Object;
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Concurrent_source_edits_have_one_winner_and_one_conflict()
    {
        await using var firstDb = Db();
        await using var secondDb = Db();
        var locked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = Pipeline<UpdateOrderCommand>(firstDb).Handle(new() { Id = 10 }, async ct =>
        {
            locked.SetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            var order = await firstDb.Orders.SingleAsync(o => o.Id == 10, ct);
            order.Notes = "First editor";
            await firstDb.SaveChangesAsync(ct);
            return new OrderDto { Id = 10 };
        }, default);
        await locked.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var secondInvoked = false;
        var second = Pipeline<UpdateOrderCommand>(secondDb).Handle(new() { Id = 10 }, _ =>
        {
            secondInvoked = true;
            return Task.FromResult(new OrderDto { Id = 10 });
        }, default);
        release.SetResult();
        await first;
        await Assert.ThrowsAsync<OrderConcurrencyException>(() => second);
        Assert.False(secondInvoked);
        await using var check = Db();
        var persisted = await check.Orders.SingleAsync(o => o.Id == 10);
        Assert.Equal("First editor", persisted.Notes);
        Assert.Equal(1, persisted.OriginBranchId);
        Assert.Equal(2, persisted.BranchId);
        Assert.True(persisted.ManagementVersion > 1);
        Assert.Single(await check.EntityAuditLogs.Where(a => a.EntityType == "order_management").ToListAsync());
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Update_and_cancel_with_inventory_join_the_parent_transaction()
    {
        long version;
        await using (var db = Db())
        {
            var rules = new Mock<IOrderBusinessRulesService>();
            rules.Setup(r => r.CanUpdateOrder(It.IsAny<Order>(), "Cashier")).Returns(true);
            var handler = new UpdateOrderHandler(new OrderRepository(db, new Clock()), db, Mock.Of<IAddressRepository>(),
                new BankPaymentRepository(db), new ReservationDepositRepository(db), Mapper(), new Actor(), rules.Object,
                Mock.Of<IOrderNotificationService>(), new Clock(), Mock.Of<IInventoryService>());
            var request = new UpdateOrderCommand { Id = 10, Order = new UpdateOrderDto { Notes = "Remote edit" } };
            var response = await Pipeline<UpdateOrderCommand>(db).Handle(request, ct => handler.Handle(request, ct), default);
            version = response.ManagementVersion;
            Assert.True(version > 1);
            Assert.Null(db.Database.CurrentTransaction);
        }
        await using (var db = Db())
        {
            var handler = new CancelOrderHandler(new OrderRepository(db, new Clock()), new BankPaymentRepository(db),
                new AppPaymentRepository(db), new ReservationDepositRepository(db), Mapper(), new Actor(),
                Mock.Of<ILoyaltyCycleService>(), Mock.Of<IDeliveryRouteWorkflowService>(), new Clock(),
                Mock.Of<IOrderNotificationService>(), db, Mock.Of<IInventoryService>());
            var request = new CancelOrderCommand { Id = 10, Cancellation = new CancelOrderDto { Reason = "Customer request" } };
            var response = await Pipeline<CancelOrderCommand>(db, version).Handle(request, ct => handler.Handle(request, ct), default);
            Assert.Equal(OrderStatus.Cancelled, response.Status);
            Assert.True(response.ManagementVersion > version);
            Assert.Null(db.Database.CurrentTransaction);
        }
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Removing_order_line_succeeds_when_detail_delete_trigger_increments_parent_version()
    {
        await using var db = Db();
        var category = new ProductCategory { BranchId = 2, Name = "Menú" };
        db.ProductCategories.Add(category);
        await db.SaveChangesAsync();
        var rice = new Product { CategoryId = category.Id, Name = "Arroz", Price = 44_000 };
        var drink = new Product { CategoryId = category.Id, Name = "Bebida", Price = 6_000 };
        db.Products.AddRange(rice, drink);
        await db.SaveChangesAsync();
        db.Orders.Add(new Order
        {
            Id = 11, BranchId = 2, OriginBranchId = 1, TakenById = 70,
            Type = OrderType.Onsite, Status = OrderStatus.Taken, StatusTimes = "{}",
            Subtotal = 50_000, Total = 50_000,
            OrderDetails =
            [
                new() { ProductId = rice.Id, Quantity = 1, UnitPrice = 44_000, Subtotal = 44_000 },
                new() { ProductId = drink.Id, Quantity = 1, UnitPrice = 6_000, Subtotal = 6_000 }
            ]
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // Mimics the production order_detail DELETE -> order totals/version trigger.
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION test_order_line_deleted_update_parent() RETURNS trigger LANGUAGE plpgsql AS $
            BEGIN
                UPDATE public."order" SET
                    subtotal = (SELECT COALESCE(SUM(subtotal), 0) FROM public.order_detail WHERE order_id = OLD.order_id),
                    total = (SELECT COALESCE(SUM(subtotal), 0) FROM public.order_detail WHERE order_id = OLD.order_id)
                      + COALESCE(delivery_fee, 0)
                WHERE id = OLD.order_id;
                RETURN OLD;
            END $;
            CREATE TRIGGER test_order_line_deleted_update_parent
                AFTER DELETE ON public.order_detail FOR EACH ROW
                EXECUTE FUNCTION test_order_line_deleted_update_parent();
            """);

        var repository = new OrderRepository(db, new Clock());
        var current = (await repository.GetByIdWithDetailsAsync(11))!;
        await using var otherDb = Db();
        var stale = (await new OrderRepository(otherDb, new Clock()).GetByIdWithDetailsAsync(11))!;
        current.OrderDetails.Remove(current.OrderDetails.Single(d => d.ProductId == drink.Id));
        current.Subtotal = 44_000;
        current.Total = 44_000;

        await repository.UpdateAsync(current);

        var saved = (await db.Orders.AsNoTracking().SingleAsync(o => o.Id == 11));
        Assert.Equal(44_000, saved.Total);
        Assert.True(saved.ManagementVersion > stale.ManagementVersion);
        Assert.Equal(rice.Id, (await db.OrderDetails.AsNoTracking()
            .SingleAsync(d => d.OrderId == 11)).ProductId);

        stale.Notes = "edición atrasada";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            new OrderRepository(otherDb, new Clock()).UpdateAsync(stale));
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Direct_SQL_updates_increment_version_and_cannot_rewrite_provenance()
    {
        await using var db = Db();
        await db.Database.ExecuteSqlRawAsync("UPDATE \"order\" SET notes = 'External update' WHERE id = 10");
        Assert.Equal(2, (await db.Orders.AsNoTracking().SingleAsync(o => o.Id == 10)).ManagementVersion);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "UPDATE \"order\" SET origin_branch_id = 2 WHERE id = 10"));
        Assert.Equal("23514", exception.SqlState);
    }
}
