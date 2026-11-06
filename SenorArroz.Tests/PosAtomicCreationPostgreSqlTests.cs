using AutoMapper;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Features.CashRegister.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.Commands;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Repositories;
using Testcontainers.PostgreSql;

namespace SenorArroz.Tests;

/// <summary>
/// Uses actual PostgreSQL transactions and the production management-version
/// trigger with an order-detail total trigger that updates the parent row.
/// Tests never access production data.
/// </summary>
public sealed class PosAtomicCreationPostgreSqlTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private DbContextOptions<ApplicationDbContext> options = null!;
    private int branchId;
    private int actorId;
    private int productId;
    private int giftId;
    private int bankId;
    private int appId;

    private sealed record Actor(int Id, int BranchId) : ICurrentUser
    {
        public int TenantId => 1;
        public string Role => "Cashier";
        public bool IsAuthenticated => true;
    }

    private sealed class Clock : IClock
    {
        public DateTime UtcNow => new(2026, 10, 15, 17, 35, 0, DateTimeKind.Utc);
    }

    private ApplicationDbContext Db() => new(options,
        currentTenant: TestTenantContext.Default, tenantExecutionContext: TestTenantContext.Default);

    private static string FindScript(string filename)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "SenorArroz.Infrastructure", "Scripts", filename);
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException(filename);
    }

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).Options;
        await using var db = Db();
        await db.Database.EnsureCreatedAsync();

        // The POS migration is additive/idempotent (run twice). Also install
        // the real existing concurrency trigger, needed to reproduce #8394.
        var idempotencyScript = await File.ReadAllTextAsync(FindScript("add_pos_creation_idempotency.sql"));
        await db.Database.ExecuteSqlRawAsync(idempotencyScript);
        await db.Database.ExecuteSqlRawAsync(idempotencyScript);
        await db.Database.ExecuteSqlRawAsync(
            await File.ReadAllTextAsync(FindScript("add_remote_order_management.sql")));

        var receiptsScript = await File.ReadAllTextAsync(FindScript("add_reservation_receipt_payment_links.sql"));
        await using (var migrationTx = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlRawAsync(receiptsScript);
            await db.Database.ExecuteSqlRawAsync(receiptsScript);
            await migrationTx.CommitAsync();
        }

        // Production order_detail triggers update the order totals; this triggers
        // management_version again. Replicate this interaction in isolated tests.
        await db.Database.ExecuteSqlRawAsync("""
            CREATE OR REPLACE FUNCTION public.test_pos_calculate_order_detail_subtotal()
            RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                NEW.subtotal := NEW.quantity * NEW.unit_price - NEW.discount;
                RETURN NEW;
            END $$;
            CREATE TRIGGER test_pos_line_subtotal_before_insert
              BEFORE INSERT ON public.order_detail
              FOR EACH ROW EXECUTE FUNCTION public.test_pos_calculate_order_detail_subtotal();

            CREATE OR REPLACE FUNCTION public.test_pos_update_order_totals()
            RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                UPDATE public."order" SET
                    subtotal = (SELECT COALESCE(SUM(subtotal), 0) FROM public.order_detail WHERE order_id = NEW.order_id),
                    discount_total = (SELECT COALESCE(SUM(discount), 0) FROM public.order_detail WHERE order_id = NEW.order_id),
                    total = (SELECT COALESCE(SUM(subtotal), 0) FROM public.order_detail WHERE order_id = NEW.order_id)
                        + COALESCE(delivery_fee, 0)
                WHERE id = NEW.order_id;
                RETURN NEW;
            END $$;
            CREATE TRIGGER test_pos_totals_after_insert AFTER INSERT ON public.order_detail
              FOR EACH ROW EXECUTE FUNCTION public.test_pos_update_order_totals();
            """);

        db.Tenants.Add(new Tenant { Id = 1, Name = "POS atomic test", Slug = "pos-atomic-test" });
        await db.SaveChangesAsync();
        var branch = new Branch { Name = "Santander", Address = "-", Phone1 = "0" };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        branchId = branch.Id;
        var user = new User
        {
            Name = "Cajero", BranchId = branch.Id,
            Email = "pos.atomic@test.local", PasswordHash = "test-only",
            Role = UserRole.Cashier, Active = true,
        };
        var category = new ProductCategory { BranchId = branch.Id, Name = "Arroces" };
        var bank = new Bank { BranchId = branch.Id, Name = "Banco", Active = true };
        db.Users.Add(user);
        db.ProductCategories.Add(category);
        db.Banks.Add(bank);
        await db.SaveChangesAsync();
        actorId = user.Id;
        bankId = bank.Id;
        var app = new App { BankId = bank.Id, Name = "App de prueba", Active = true };
        db.Apps.Add(app);
        await db.SaveChangesAsync();
        appId = app.Id;
        var rice = new Product { CategoryId = category.Id, Name = "Arroz Paisa", Price = 35000 };
        var gift = new Product { CategoryId = category.Id, Name = "Yucas gratis", Price = 0 };
        db.Products.AddRange(rice, gift);
        await db.SaveChangesAsync();
        productId = rice.Id;
        giftId = gift.Id;
    }

    public async Task DisposeAsync() => await postgres.DisposeAsync();

    private CreateOrderDto Request(string key, int cash = 25000, bool includeBank = true) => new()
    {
        PosCreationKey = key,
        BranchId = branchId,
        TakenById = actorId,
        Type = OrderType.Onsite,
        GuestName = "Prueba",
        PaidInStoreCash = true,
        PaidInStoreCashAmount = cash,
        OrderDetails =
        [
            new CreateOrderDetailDto { ProductId = productId, Quantity = 1, UnitPrice = 35000 },
            new CreateOrderDetailDto { ProductId = giftId, Quantity = 1, UnitPrice = 0 },
        ],
        BankPayments = includeBank ? [new CreateOrderBankPaymentDto { BankId = bankId, Amount = 10000 }] : [],
    };

    private CreateOrderHandler Handler(
        ApplicationDbContext db,
        IInventoryService? inventory = null,
        IOrderNotificationService? notifications = null)
    {
        var mapper = new MapperConfiguration(
            cfg => cfg.AddMaps(typeof(CreateOrderCommand).Assembly),
            NullLoggerFactory.Instance).CreateMapper();
        var notice = notifications ?? Mock.Of<IOrderNotificationService>();
        return new CreateOrderHandler(new OrderRepository(db, new Clock()), db, mapper,
            new Actor(actorId, branchId), notice, new Clock(), inventory: inventory);
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Two_product_triggers_and_cash_commit_as_one_order_and_same_key_replays()
    {
        var key = Guid.NewGuid().ToString("D");
        var payload = Request(key);
        int createdId;
        await using (var db = Db())
        {
            var created = await Handler(db).Handle(new CreateOrderCommand { Order = payload }, default);
            createdId = created.Id;
            Assert.Equal(35000, created.Total);
            Assert.True(created.PaidInStoreCash);
            Assert.Equal(25000, created.PaidInStoreCashAmount);
        }
        await using (var db = Db())
        {
            var again = await Handler(db).Handle(new CreateOrderCommand { Order = Request(key) }, default);
            Assert.Equal(createdId, again.Id);
        }
        await using (var db = Db())
        {
            var saved = await db.Orders.AsNoTracking().SingleAsync(o => o.PosCreationKey == key);
            Assert.Equal(createdId, saved.Id);
            Assert.True(saved.ManagementVersion >= 4); // 2 line triggers + the cash update.
            Assert.True(saved.PaidInStoreCash);
            Assert.Equal(25000, saved.PaidInStoreCashAmount);
            Assert.Equal(2, await db.OrderDetails.CountAsync(d => d.OrderId == saved.Id));
            Assert.Single(await db.BankPayments.Where(p => p.OrderId == saved.Id).ToListAsync());
            Assert.Equal(1, await db.Orders.CountAsync(o => o.PosCreationKey == key));
        }
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Invalid_cash_after_order_and_bank_insert_rolls_back_everything()
    {
        var key = Guid.NewGuid().ToString("D");
        await using (var db = Db())
        {
            await Assert.ThrowsAsync<BusinessException>(() => Handler(db).Handle(
                new CreateOrderCommand { Order = Request(key, cash: 999999) }, default));
        }
        await using (var db = Db())
        {
            Assert.False(await db.Orders.AnyAsync(o => o.PosCreationKey == key));
            Assert.Empty(await db.OrderDetails.ToListAsync());
            Assert.Empty(await db.BankPayments.ToListAsync());
        }
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Inventory_failure_rolls_back_parent_and_detail_in_same_transaction()
    {
        var key = Guid.NewGuid().ToString("D");
        var inventory = new Mock<IInventoryService>();
        inventory.Setup(x => x.SnapshotAndReserveAsync(It.IsAny<Order>(), It.IsAny<bool>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException("Inventario de prueba rechazado"));
        await using (var db = Db())
        {
            await Assert.ThrowsAsync<BusinessException>(() => Handler(db, inventory.Object).Handle(
                new CreateOrderCommand { Order = Request(key) }, default));
        }
        await using (var db = Db())
        {
            Assert.False(await db.Orders.AnyAsync(o => o.PosCreationKey == key));
            Assert.Empty(await db.OrderDetails.ToListAsync());
            Assert.Empty(await db.BankPayments.ToListAsync());
        }
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Reused_uuid_with_modified_payload_is_rejected_without_new_order()
    {
        var key = Guid.NewGuid().ToString("D");
        await using (var db = Db())
            await Handler(db).Handle(new CreateOrderCommand { Order = Request(key) }, default);
        await using (var db = Db())
        {
            var changed = Request(key);
            changed.OrderDetails[0].Quantity = 2;
            await Assert.ThrowsAsync<BusinessException>(() =>
                Handler(db).Handle(new CreateOrderCommand { Order = changed }, default));
        }
        await using var verify = Db();
        Assert.Equal(1, await verify.Orders.CountAsync(o => o.PosCreationKey == key));
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Notification_failure_after_commit_never_returns_an_error_to_retrigger_creation()
    {
        var key = Guid.NewGuid().ToString("D");
        var notifications = new Mock<IOrderNotificationService>();
        notifications.Setup(x => x.NotifyNewOrderToKitchen(It.IsAny<OrderDto>()))
            .ThrowsAsync(new InvalidOperationException("Simulated hub failure"));
        await using var db = Db();
        var created = await Handler(db, notifications: notifications.Object).Handle(
            new CreateOrderCommand { Order = Request(key) }, default);
        Assert.True(created.Id > 0);
        await using var verify = Db();
        Assert.Single(await verify.Orders.Where(o => o.PosCreationKey == key).ToListAsync());
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Concurrent_duplicate_uuid_requests_have_one_persisted_order()
    {
        var key = Guid.NewGuid().ToString("D");
        await using var first = Db();
        await using var second = Db();
        var requests = await Task.WhenAll(
            Handler(first).Handle(new CreateOrderCommand { Order = Request(key) }, default),
            Handler(second).Handle(new CreateOrderCommand { Order = Request(key) }, default));
        Assert.Equal(requests[0].Id, requests[1].Id);
        await using var verify = Db();
        Assert.Single(await verify.Orders.Where(o => o.PosCreationKey == key).ToListAsync());
    }
    private CreateOrderDto ReservationRequest(string key, int? cash = 5000)
    {
        var request = Request(key, cash: cash ?? 0);
        request.Type = OrderType.Reservation;
        request.ReservedFor = new Clock().UtcNow.AddDays(1);
        request.PrepareAt = request.ReservedFor.Value.AddHours(-1);
        request.PaidInStoreCash = cash.HasValue;
        request.PaidInStoreCashAmount = cash;
        request.AppPayments = [new CreateOrderAppPaymentDto { AppId = appId, Amount = 5000 }];
        return request;
    }

    [PostgreSqlIntegrationFact]
    public async Task Reservation_mixed_receipts_commit_once_and_replay_includes_deposits()
    {
        var key = Guid.NewGuid().ToString("D");
        int id;
        await using (var db = Db())
        {
            var dto = await Handler(db).Handle(new CreateOrderCommand { Order = ReservationRequest(key) }, default);
            id = dto.Id;
            Assert.Equal(OrderType.Reservation, dto.Type);
            Assert.Equal(20000m, dto.TotalDeposited);
            Assert.Equal(3, dto.ReservationDeposits.Count);
            Assert.All(dto.ReservationDeposits, d => Assert.Equal(new Clock().UtcNow, d.ReceivedAt));
            Assert.Equal(5000, dto.PaidInStoreCashAmount);
            Assert.True(dto.ReservationDeposits.Single(d => d.IsEffective).CashAppliedToOrder);
        }
        await using (var db = Db())
        {
            var replay = await Handler(db).Handle(new CreateOrderCommand { Order = ReservationRequest(key) }, default);
            Assert.Equal(id, replay.Id);
            Assert.Equal(20000m, replay.TotalDeposited);
            Assert.Equal(3, replay.ReservationDeposits.Count);
        }
        await using var verify = Db();
        var order = await ReservationReceiptHelper.LoadPaymentsAsync(verify, id, default);
        Assert.Equal(3, order.Deposits.Count);
        Assert.NotNull(Assert.Single(order.BankPayments).SourceReservationDepositId);
        Assert.NotNull(Assert.Single(order.AppPayments).SourceReservationDepositId);
        Assert.Equal(15000m, OrderCashPortionHelper.GetCashPortion(order));
        var retained = await CashRegisterUnsettledAppsHelper.LoadUnsettledForBranchAsync(verify, branchId, default);
        Assert.Equal(5000m, retained.Total); // received now, not only after tomorrow's delivery
        Assert.Single(await verify.Orders.Where(o => o.PosCreationKey == key).ToListAsync());
    }

    [PostgreSqlIntegrationFact]
    public async Task Reservation_without_explicit_cash_never_assumes_pending_balance_received()
    {
        await using var db = Db();
        var dto = await Handler(db).Handle(new CreateOrderCommand { Order = ReservationRequest(Guid.NewGuid().ToString("D"), null) }, default);
        Assert.False(dto.PaidInStoreCash);
        Assert.Equal(15000m, dto.TotalDeposited);
        Assert.Equal(2, dto.ReservationDeposits.Count);
        var order = await ReservationReceiptHelper.LoadPaymentsAsync(db, dto.Id, default);
        Assert.Equal(20000m, OrderCashPortionHelper.GetCashPortion(order));
    }

    [PostgreSqlIntegrationFact]
    public async Task Invalid_reservation_coverage_rolls_back_order_lines_and_every_receipt()
    {
        var key = Guid.NewGuid().ToString("D");
        await using (var db = Db())
            await Assert.ThrowsAsync<BusinessException>(() => Handler(db).Handle(
                new CreateOrderCommand { Order = ReservationRequest(key, 30000) }, default));
        await using var verify = Db();
        Assert.Empty(await verify.Orders.ToListAsync());
        Assert.Empty(await verify.OrderDetails.ToListAsync());
        Assert.Empty(await verify.ReservationDeposits.ToListAsync());
        Assert.Empty(await verify.BankPayments.ToListAsync());
        Assert.Empty(await verify.AppPayments.ToListAsync());
    }

    [PostgreSqlIntegrationFact]
    public async Task Concurrent_reservation_retry_never_duplicates_receipts()
    {
        var key = Guid.NewGuid().ToString("D");
        await using var first = Db();
        await using var second = Db();
        var results = await Task.WhenAll(
            Handler(first).Handle(new CreateOrderCommand { Order = ReservationRequest(key) }, default),
            Handler(second).Handle(new CreateOrderCommand { Order = ReservationRequest(key) }, default));
        Assert.Equal(results[0].Id, results[1].Id);
        await using var verify = Db();
        Assert.Equal(3, await verify.ReservationDeposits.CountAsync());
        Assert.Single(await verify.BankPayments.ToListAsync());
        Assert.Single(await verify.AppPayments.ToListAsync());
    }

    [PostgreSqlIntegrationFact]
    public async Task Editing_and_deleting_receipts_synchronizes_operational_cash_and_apps()
    {
        int id;
        await using (var db = Db())
            id = (await Handler(db).Handle(new CreateOrderCommand { Order = ReservationRequest(Guid.NewGuid().ToString("D")) }, default)).Id;
        await using (var db = Db())
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            var deposits = await db.ReservationDeposits.Where(d => d.OrderId == id).ToListAsync();
            await ReservationReceiptHelper.UpdateAsync(db, deposits.Single(d => d.IsEffective), 7000m, default);
            await ReservationReceiptHelper.UpdateAsync(db, deposits.Single(d => d.AppId.HasValue), 6000m, default);
            await tx.CommitAsync();
        }
        await using (var db = Db())
        {
            var order = await ReservationReceiptHelper.LoadPaymentsAsync(db, id, default);
            Assert.Equal(7000, order.PaidInStoreCashAmount);
            Assert.Equal(6000m, Assert.Single(order.AppPayments).Amount);
            Assert.Equal(12000m, OrderCashPortionHelper.GetCashPortion(order));
            await using var tx = await db.Database.BeginTransactionAsync();
            await ReservationReceiptHelper.DeleteAsync(db, order.Deposits.Single(d => d.AppId.HasValue), default);
            await ReservationReceiptHelper.DeleteAsync(db, order.Deposits.Single(d => d.IsEffective), default);
            await tx.CommitAsync();
        }
        await using var verify = Db();
        var saved = await ReservationReceiptHelper.LoadPaymentsAsync(verify, id, default);
        Assert.False(saved.PaidInStoreCash);
        Assert.Null(saved.PaidInStoreCashAmount);
        Assert.Empty(saved.AppPayments);
        Assert.Single(saved.Deposits);
        Assert.Equal(25000m, OrderCashPortionHelper.GetCashPortion(saved));
    }

    [PostgreSqlIntegrationFact]
    public async Task Fully_paid_cash_reservation_and_deletion_keep_zero_or_full_balance()
    {
        var request = ReservationRequest(Guid.NewGuid().ToString("D"), 35000);
        request.BankPayments.Clear(); request.AppPayments.Clear();
        await using var db = Db();
        var dto = await Handler(db).Handle(new CreateOrderCommand { Order = request }, default);
        Assert.Equal(35000m, dto.TotalDeposited);
        var order = await ReservationReceiptHelper.LoadPaymentsAsync(db, dto.Id, default);
        Assert.Equal(0m, OrderCashPortionHelper.GetCashPortion(order));
        await using var tx = await db.Database.BeginTransactionAsync();
        await ReservationReceiptHelper.DeleteAsync(db, Assert.Single(order.Deposits), default);
        await tx.CommitAsync();
        Assert.Equal(35000m, OrderCashPortionHelper.GetCashPortion(order));
    }

}
