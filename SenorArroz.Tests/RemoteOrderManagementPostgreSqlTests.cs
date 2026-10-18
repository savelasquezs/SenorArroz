using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using SenorArroz.Application.Common.Behaviors;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Features.Inventory.Services;
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
    private PostgreSqlContainer? postgres;
    private string? serverConnectionString;
    private string? temporaryDatabaseName;
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
        string connectionString;
        var sharedConnectionString = Environment.GetEnvironmentVariable("FLOW_POSTGRES_TEST_CONNECTION");
        if (!string.IsNullOrWhiteSpace(sharedConnectionString))
        {
            // Never initialize or reset the supplied database. Each test owns a fresh database.
            temporaryDatabaseName = $"remote_orders_test_{Guid.NewGuid():N}";
            var builder = new NpgsqlConnectionStringBuilder(sharedConnectionString) { Database = "postgres", Pooling = false };
            serverConnectionString = builder.ConnectionString;
            await using var server = new NpgsqlConnection(serverConnectionString);
            await server.OpenAsync();
            await new NpgsqlCommand($"CREATE DATABASE {temporaryDatabaseName}", server).ExecuteNonQueryAsync();
            builder.Database = temporaryDatabaseName;
            connectionString = builder.ConnectionString;
        }
        else
        {
            postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
            await postgres.StartAsync();
            connectionString = postgres.GetConnectionString();
        }
        options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options;
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
    public async Task DisposeAsync()
    {
        if (serverConnectionString is not null && temporaryDatabaseName is not null)
        {
            await using var server = new NpgsqlConnection(serverConnectionString);
            await server.OpenAsync();
            await new NpgsqlCommand($"DROP DATABASE IF EXISTS {temporaryDatabaseName} WITH (FORCE)", server).ExecuteNonQueryAsync();
        }
        if (postgres is not null) await postgres.DisposeAsync();
    }
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
        await InstallDetailDeleteTriggerAsync(db);

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
    public Task Adding_order_lines_preserves_existing_and_new_lines_and_inventory()
        => VerifyLineEditAsync(keepRice: true, keepDrink: true, addDrink: true);

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public Task Updating_adding_and_removing_lines_preserves_totals_and_inventory()
        => VerifyLineEditAsync(keepRice: true, keepDrink: false, addDrink: true, riceQuantity: 2);

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public Task Replacing_all_lines_keeps_new_lines_instead_of_emptying_the_order()
        => VerifyLineEditAsync(keepRice: false, keepDrink: false, addDrink: true);

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public Task Removing_a_line_through_the_handler_preserves_version_and_releases_inventory()
        => VerifyLineEditAsync(keepRice: true, keepDrink: false, addDrink: false);

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public Task Failed_inventory_reservation_rolls_back_added_removed_lines_and_totals()
        => VerifyLineEditAsync(keepRice: true, keepDrink: false, addDrink: true, insufficientStock: true);

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public Task Removing_every_line_without_a_replacement_is_rejected_without_changes()
        => VerifyLineEditAsync(keepRice: false, keepDrink: false, addDrink: false);

    private async Task VerifyLineEditAsync(bool keepRice, bool keepDrink, bool addDrink,
        int riceQuantity = 1, bool insufficientStock = false)
    {
        await using var db = Db();
        var category = new ProductCategory { BranchId = 2, Name = "Menú" };
        var expenseCategory = new ExpenseCategory { Name = "Bebidas" };
        db.AddRange(category, expenseCategory);
        await db.SaveChangesAsync();
        var rice = new Product { CategoryId = category.Id, Name = "Arroz", Price = 44_000 };
        var oldDrink = new Product { CategoryId = category.Id, Name = "Bebida anterior", Price = 6_000,
            InventoryEnabled = true, InventoryControlMode = InventoryControlMode.Strict };
        var newDrink = new Product { CategoryId = category.Id, Name = "Bebida nueva", Price = 7_000,
            InventoryEnabled = true, InventoryControlMode = InventoryControlMode.Strict };
        var oldExpense = new Expense { CategoryId = expenseCategory.Id, Name = "Botella anterior", TracksInventory = true, InventoryActive = true };
        var newExpense = new Expense { CategoryId = expenseCategory.Id, Name = "Botella nueva", TracksInventory = true, InventoryActive = true };
        db.AddRange(rice, oldDrink, newDrink, oldExpense, newExpense);
        await db.SaveChangesAsync();
        db.AddRange(
            new ProductExpenseRequirement { ProductId = oldDrink.Id, ExpenseId = oldExpense.Id, BaseQuantity = 1 },
            new ProductExpenseRequirement { ProductId = newDrink.Id, ExpenseId = newExpense.Id, BaseQuantity = 1 },
            new InventoryBalance { BranchId = 2, ExpenseId = oldExpense.Id, QuantityOnHand = 10 },
            new InventoryBalance { BranchId = 2, ExpenseId = newExpense.Id, QuantityOnHand = insufficientStock ? 1 : 10 });
        var order = await db.Orders.SingleAsync(o => o.Id == 10);
        order.DeliveryFee = 3_000;
        order.OrderDetails =
        [
            new() { ProductId = rice.Id, Quantity = 1, UnitPrice = rice.Price, Subtotal = rice.Price },
            new() { ProductId = oldDrink.Id, Quantity = 1, UnitPrice = oldDrink.Price, Subtotal = oldDrink.Price }
        ];
        OrderTotalsHelper.RecalculateFromOrderDetails(order);
        await db.SaveChangesAsync();
        var inventory = new InventoryService(db, new Actor(), TestTenantContext.Default, new Clock(), new TestBranchContext(1));
        await inventory.SnapshotAndReserveAsync(order, false, "seed:reservation", default);
        db.ChangeTracker.Clear();
        await InstallDetailDeleteTriggerAsync(db);

        var repository = new OrderRepository(db, new Clock());
        var before = (await repository.GetByIdWithDetailsAsync(10))!;
        var originalLines = before.OrderDetails.OrderBy(d => d.Id).ToArray();
        var incoming = new List<UpdateOrderDetailDto>();
        if (keepRice)
            incoming.Add(new() { Id = originalLines.Single(d => d.ProductId == rice.Id).Id,
                ProductId = rice.Id, Quantity = riceQuantity, UnitPrice = rice.Price });
        if (keepDrink)
            incoming.Add(new() { Id = originalLines.Single(d => d.ProductId == oldDrink.Id).Id,
                ProductId = oldDrink.Id, Quantity = 1, UnitPrice = oldDrink.Price });
        if (addDrink)
            incoming.Add(new() { ProductId = newDrink.Id, Quantity = 2, UnitPrice = newDrink.Price });

        var mapper = new MapperConfiguration(cfg => cfg.AddMaps(typeof(UpdateOrderCommand).Assembly), NullLoggerFactory.Instance).CreateMapper();
        var handler = new UpdateOrderHandler(repository, db, Mock.Of<IAddressRepository>(),
            new BankPaymentRepository(db), new ReservationDepositRepository(db), mapper, new Actor(),
            new OrderBusinessRulesService(new Clock()), Mock.Of<IOrderNotificationService>(), new Clock(), inventory);
        var request = new UpdateOrderCommand { Id = 10, Order = new UpdateOrderDto { OrderDetails = incoming } };
        var shouldReject = insufficientStock || incoming.Count == 0;
        OrderDto? response = null;
        if (shouldReject)
        {
            var error = await Assert.ThrowsAsync<BusinessException>(() => Pipeline<UpdateOrderCommand>(db, before.ManagementVersion)
                .Handle(request, ct => handler.Handle(request, ct), default));
            Assert.Contains(insufficientStock ? "inventario suficiente" : "al menos un producto", error.Message);
        }
        else
        {
            response = await Pipeline<UpdateOrderCommand>(db, before.ManagementVersion)
                .Handle(request, ct => handler.Handle(request, ct), default);
        }
        Assert.Null(db.Database.CurrentTransaction);

        // A fresh context ensures tracking cannot hide rows deleted by ExecuteDeleteAsync.
        await using var check = Db();
        var saved = (await new OrderRepository(check, new Clock()).GetByIdWithDetailsAsync(10))!;
        var expectedLines = shouldReject
            ? originalLines.Select(d => (d.ProductId, d.Quantity, d.UnitPrice)).OrderBy(d => d.ProductId).ToArray()
            : incoming.Select(d => (d.ProductId, d.Quantity, d.UnitPrice)).OrderBy(d => d.ProductId).ToArray();
        Assert.Equal(expectedLines, saved.OrderDetails.Select(d => (d.ProductId, d.Quantity, d.UnitPrice)).OrderBy(d => d.ProductId).ToArray());
        Assert.All(saved.OrderDetails, d => Assert.True(d.Id > 0));
        Assert.Equal(expectedLines.Sum(d => d.Quantity * d.UnitPrice), saved.Subtotal);
        Assert.Equal(saved.Subtotal + 3_000, saved.Total);
        Assert.Equal(0, saved.DiscountTotal);
        Assert.Equal(1, saved.TenantId);
        Assert.Equal(2, saved.BranchId);
        Assert.Equal(1, saved.OriginBranchId);
        foreach (var original in originalLines)
        {
            var retained = shouldReject || incoming.Any(d => d.Id == original.Id);
            Assert.Equal(retained, saved.OrderDetails.Any(d => d.Id == original.Id));
        }
        if (addDrink && !shouldReject)
            Assert.DoesNotContain(saved.OrderDetails.Single(d => d.ProductId == newDrink.Id).Id, originalLines.Select(d => d.Id));

        var allocations = await check.OrderInventoryAllocations.Where(a => a.OrderId == 10).ToArrayAsync();
        var expectedAllocations = expectedLines.Where(d => d.ProductId != rice.Id).OrderBy(d => d.ProductId).ToArray();
        Assert.Equal(expectedAllocations.Select(d => (d.ProductId, (decimal)d.Quantity)),
            allocations.OrderBy(a => a.ProductId).Select(a => (a.ProductId, a.ReservedQuantity)));
        var balances = await check.InventoryBalances.Where(b => b.BranchId == 2).ToDictionaryAsync(b => b.ExpenseId);
        Assert.Equal(expectedAllocations.Where(d => d.ProductId == oldDrink.Id).Sum(d => d.Quantity), balances[oldExpense.Id].QuantityReserved);
        Assert.Equal(expectedAllocations.Where(d => d.ProductId == newDrink.Id).Sum(d => d.Quantity), balances[newExpense.Id].QuantityReserved);
        Assert.Equal(10, balances[oldExpense.Id].QuantityOnHand);
        Assert.Equal(insufficientStock ? 1 : 10, balances[newExpense.Id].QuantityOnHand);

        if (shouldReject)
        {
            Assert.Equal(before.ManagementVersion, saved.ManagementVersion);
            Assert.Empty(await check.EntityAuditLogs.Where(a => a.EntityType == "order_management").ToArrayAsync());
            return;
        }
        Assert.Equal(saved.Total, response!.Total);
        Assert.Equal(saved.ManagementVersion, response.ManagementVersion);
        Assert.True(saved.ManagementVersion > before.ManagementVersion);
        Assert.Single(await check.EntityAuditLogs.Where(a => a.EntityType == "order_management").ToArrayAsync());
        await using var staleDb = Db();
        var staleHandlerInvoked = false;
        await Assert.ThrowsAsync<OrderConcurrencyException>(() => Pipeline<UpdateOrderCommand>(staleDb, before.ManagementVersion)
            .Handle(request, _ => { staleHandlerInvoked = true; return Task.FromResult(new OrderDto()); }, default));
        Assert.False(staleHandlerInvoked);
    }

    private static Task InstallDetailDeleteTriggerAsync(ApplicationDbContext db) => db.Database.ExecuteSqlRawAsync("""
        CREATE FUNCTION test_order_line_deleted_update_parent() RETURNS trigger LANGUAGE plpgsql AS $body$
        BEGIN
            UPDATE public."order" SET
                subtotal = (SELECT COALESCE(SUM(subtotal), 0) FROM public.order_detail WHERE order_id = OLD.order_id),
                total = (SELECT COALESCE(SUM(subtotal), 0) FROM public.order_detail WHERE order_id = OLD.order_id)
                  + COALESCE(delivery_fee, 0)
            WHERE id = OLD.order_id;
            RETURN OLD;
        END $body$;
        CREATE TRIGGER test_order_line_deleted_update_parent
            AFTER DELETE ON public.order_detail FOR EACH ROW
            EXECUTE FUNCTION test_order_line_deleted_update_parent();
        """);


    private sealed record SuperadminActor : ICurrentUser
    {
        public int Id => 70;
        public int TenantId => 1;
        public int BranchId => 1;
        public string Role => "Superadmin";
        public bool IsAuthenticated => true;
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Paid_delivered_order_transfer_changes_only_dispatch_branch_and_preserves_consumed_inventory()
    {
        await using var db = Db();
        var catalog = new ProductCategory { BranchId = 2, Name = "Menú" };
        var expenseCategory = new ExpenseCategory { Name = "Insumos" };
        var bank = new Bank { BranchId = 2, Name = "Banco origen" };
        db.AddRange(catalog, expenseCategory, bank);
        await db.SaveChangesAsync();

        var product = new Product { CategoryId = catalog.Id, Name = "Arroz", Price = 30_000,
            InventoryEnabled = true, InventoryControlMode = InventoryControlMode.Strict };
        var expense = new Expense { CategoryId = expenseCategory.Id, Name = "Arroz crudo",
            TracksInventory = true, InventoryActive = true };
        db.AddRange(product, expense);
        await db.SaveChangesAsync();
        db.Add(new ProductExpenseRequirement { ProductId = product.Id, ExpenseId = expense.Id, BaseQuantity = 1 });
        db.Add(new InventoryBalance { BranchId = 2, ExpenseId = expense.Id, QuantityOnHand = 10 });

        var order = await db.Orders.SingleAsync(o => o.Id == 10);
        order.OrderDetails = [new OrderDetail { ProductId = product.Id, Quantity = 1,
            UnitPrice = 30_000, Subtotal = 30_000 }];
        order.Subtotal = 30_000;
        order.Total = 30_000;
        db.BankPayments.Add(new BankPayment { OrderId = order.Id, BankId = bank.Id,
            Amount = 30_000, IsVerified = true });
        await db.SaveChangesAsync();
        var inventory = new InventoryService(db, new Actor(), TestTenantContext.Default,
            new Clock(), new TestBranchContext(1));
        await inventory.SnapshotAndReserveAsync(order, false, "transfer:test:reserve", default);
        await inventory.ConsumeOrderAsync(order, "transfer:test:consume", default);
        order.Status = OrderStatus.Delivered;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var version = (await db.Orders.AsNoTracking().SingleAsync(o => o.Id == 10)).ManagementVersion;
        var allocationsBefore = await db.OrderInventoryAllocations.AsNoTracking()
            .Where(a => a.OrderId == 10).Select(a => new { a.Id, a.ProductId,
                a.ReservedQuantity, a.ConsumedQuantity }).ToArrayAsync();
        var movementsBefore = await db.InventoryMovements.AsNoTracking()
            .Where(m => m.OrderId == 10).Select(m => new { m.Id, m.BranchId,
                m.ExpenseId, m.OnHandDelta, m.ReservedDelta }).ToArrayAsync();
        var balanceBefore = await db.InventoryBalances.AsNoTracking()
            .Where(b => b.BranchId == 2 && b.ExpenseId == expense.Id)
            .Select(b => new { b.QuantityOnHand, b.QuantityReserved }).SingleAsync();

        var actor = new SuperadminActor();
        var branch = new TestBranchContext(1);
        var mutation = new Snapshot(version);
        var notifications = new Mock<IOrderNotificationService>();
        notifications.Setup(n => n.NotifyOrderTransferred(10, 2, 1)).Returns(Task.CompletedTask);
        notifications.Setup(n => n.NotifyOrderManagementChanged(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<long>()))
            .Returns(Task.CompletedTask);

        var handler = new TransferOrderBranchHandler(db, new OrderRepository(db, new Clock()),
            actor, branch, mutation, notifications.Object, Mapper(), new Clock());
        var pipeline = new OrderMutationBehavior<TransferOrderBranchCommand, OrderDto>(
            db, actor, branch, notifications.Object,
            NullLogger<OrderMutationBehavior<TransferOrderBranchCommand, OrderDto>>.Instance,
            mutation, new Clock());
        var transfer = new TransferOrderBranchCommand { Id = 10, TargetBranchId = 1 };
        var result = await pipeline.Handle(transfer, ct => handler.Handle(transfer, ct), default);
        Assert.Equal(1, result.BranchId);
        Assert.Null(db.Database.CurrentTransaction);

        await using var check = Db();
        var saved = await check.Orders.AsNoTracking().SingleAsync(o => o.Id == 10);
        var payment = await check.BankPayments.Include(p => p.Bank)
            .AsNoTracking().SingleAsync(p => p.OrderId == 10);
        var line = await check.OrderDetails.AsNoTracking().SingleAsync(d => d.OrderId == 10);
        Assert.Equal(1, saved.BranchId);
        Assert.Equal(1, saved.OriginBranchId);
        Assert.Equal(OrderStatus.Delivered, saved.Status);
        Assert.Equal(30_000, saved.Total);
        Assert.True(saved.ManagementVersion > version);
        Assert.Equal(bank.Id, payment.BankId);
        Assert.Equal(2, payment.Bank.BranchId);
        Assert.True(payment.IsVerified);
        Assert.Equal(product.Id, line.ProductId);
        Assert.Equal(allocationsBefore, await check.OrderInventoryAllocations.AsNoTracking()
            .Where(a => a.OrderId == 10).Select(a => new { a.Id, a.ProductId,
                a.ReservedQuantity, a.ConsumedQuantity }).ToArrayAsync());
        Assert.Equal(movementsBefore, await check.InventoryMovements.AsNoTracking()
            .Where(m => m.OrderId == 10).Select(m => new { m.Id, m.BranchId,
                m.ExpenseId, m.OnHandDelta, m.ReservedDelta }).ToArrayAsync());
        Assert.Equal(balanceBefore, await check.InventoryBalances.AsNoTracking()
            .Where(b => b.BranchId == 2 && b.ExpenseId == expense.Id)
            .Select(b => new { b.QuantityOnHand, b.QuantityReserved }).SingleAsync());
        Assert.Single(await check.EntityAuditLogs.Where(a => a.EntityType == "order_management").ToArrayAsync());
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
