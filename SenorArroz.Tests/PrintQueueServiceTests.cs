using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Services;
using SenorArroz.Shared.Models.Printing;
using Testcontainers.PostgreSql;
using SenorArroz.Tests.Support;

namespace SenorArroz.Tests;

public class PrintQueueServiceTests
{
    [Fact]
    public async Task Delivery_job_is_saved_before_notification()
    {
        await using var db = CreateDb();
        SeedDelivery(db);
        var notifier = new RecordingNotifier(db);
        var service = CreateService(db, notifier);

        var job = await service.EnqueueDeliveryAsync(1, [10]);

        Assert.True(job.Id > 0);
        Assert.Equal(job.Id, notifier.JobId);
        Assert.True(notifier.JobWasPersistedWhenCalled);
    }

    [Fact]
    public async Task SignalR_failure_does_not_invalidate_persisted_job()
    {
        await using var db = CreateDb();
        SeedDelivery(db);
        var service = CreateService(db, new ThrowingNotifier());

        var job = await service.EnqueueDeliveryAsync(1, [10]);

        Assert.True(job.Id > 0);
        Assert.Equal(1, await db.PrintJobs.CountAsync());
        Assert.Equal(PrintJobStatus.Pending, (await db.PrintJobs.SingleAsync()).Status);
    }

    [Theory]
    [InlineData(PrintJobKind.Kitchen)]
    [InlineData(PrintJobKind.Cashier)]
    public async Task Enqueue_keeps_notes_and_loyalty_for_multiple_customers(PrintJobKind kind)
    {
        await using var db = CreateDb();
        var branch = new Branch { Id = 1, Name = "Sucursal" };
        db.Branches.Add(branch);
        db.BranchPrintSettings.Add(new BranchPrintSettings
        {
            BranchId = 1, Branch = branch, EnableKitchenJobs = true, EnableCashierJobs = true,
        });
        db.Customers.AddRange(
            new Customer { Id = 41, BranchId = 1, Name = "A", Branch = branch },
            new Customer { Id = 42, BranchId = 1, Name = "B", Branch = branch });
        db.LoyaltyCycleSteps.Add(new LoyaltyCycleStep
        {
            Id = 1, BranchId = 1, Branch = branch, StepIndex = 1, RewardLabel = "Premio",
        });

        Order MakeOrder(int id, int? customerId, OrderStatus status, string? notes = null) => new()
        {
            Id = id, BranchId = 1, TakenById = 1, CustomerId = customerId,
            Type = OrderType.Onsite, Status = status, Notes = notes,
            Total = 20000, CreatedAt = DateTime.UtcNow, Branch = branch,
        };
        db.Orders.AddRange(
            MakeOrder(10, 41, OrderStatus.Taken, "Sin cebolla"),
            MakeOrder(20, 42, OrderStatus.Taken, "Extra salsa"),
            MakeOrder(30, null, OrderStatus.Taken, "Mesa"),
            MakeOrder(11, 41, OrderStatus.Delivered),
            MakeOrder(12, 41, OrderStatus.Delivered),
            MakeOrder(21, 42, OrderStatus.Delivered));
        await db.SaveChangesAsync();

        var job = await CreateService(db, new RecordingNotifier(db))
            .EnqueueAsync(1, kind, [10, 20, 30]);
        var payload = JsonSerializer.Deserialize<PrintTicketPayloadBatchV1>(
            job.PayloadJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var orders = payload.Orders.OrderBy(o => o.OrderId).ToArray();

        Assert.Equal(new[] { "Sin cebolla", "Extra salsa", "Mesa" },
            orders.Select(o => o.OrderNotes));
        Assert.All(orders, o => Assert.Equal(o.OrderNotes, o.OrderLevelNotes));
        Assert.Equal(2, orders[0].LoyaltyDeliveredCount);
        Assert.Equal(1, orders[1].LoyaltyDeliveredCount);
        Assert.Null(orders[2].LoyaltyDeliveredCount);
        Assert.Equal("Premio", orders[0].LoyaltyNextRewardLabel);
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Kitchen_enqueue_queries_batch_loyalty_and_notes_on_PostgreSql()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).Options;
        await using var db = new ApplicationDbContext(options,
            currentTenant: TestTenantContext.Default, tenantExecutionContext: TestTenantContext.Default);
        await db.Database.EnsureCreatedAsync();

        db.Tenants.Add(new Tenant { Id = 1, Name = "Printing", Slug = "print-test" });
        var branch = new Branch { Name = "Sucursal", Address = "-", Phone1 = "0" };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        var cashier = new User
        {
            BranchId = branch.Id, Name = "Caja", Email = "print@test.local",
            PasswordHash = "test-only", Role = UserRole.Cashier,
        };
        var customer = new Customer { BranchId = branch.Id, Name = "Cliente" };
        db.Users.Add(cashier);
        db.Customers.Add(customer);
        db.BranchPrintSettings.Add(new BranchPrintSettings
        {
            BranchId = branch.Id, EnableKitchenJobs = true,
        });
        db.LoyaltyCycleSteps.Add(new LoyaltyCycleStep
        {
            BranchId = branch.Id, StepIndex = 1, RewardLabel = "Premio",
        });
        await db.SaveChangesAsync();
        var order = new Order
        {
            BranchId = branch.Id, TakenById = cashier.Id, CustomerId = customer.Id,
            Status = OrderStatus.Taken, Type = OrderType.Onsite,
            Notes = "Sin cebolla", StatusTimes = "{}", CreatedAt = DateTime.UtcNow,
        };
        db.Orders.AddRange(order, new Order
        {
            BranchId = branch.Id, TakenById = cashier.Id, CustomerId = customer.Id,
            Status = OrderStatus.Delivered, Type = OrderType.Onsite,
            StatusTimes = "{}", CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var job = await CreateService(db, new RecordingNotifier(db))
            .EnqueueAsync(branch.Id, PrintJobKind.Kitchen, [order.Id]);
        var payload = JsonSerializer.Deserialize<PrintTicketPayloadBatchV1>(
            job.PayloadJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var printed = Assert.Single(payload.Orders);
        Assert.Equal("Sin cebolla", printed.OrderNotes);
        Assert.Equal(1, printed.LoyaltyDeliveredCount);
        Assert.Equal("Premio", printed.LoyaltyNextRewardLabel);
    }

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, currentTenant: TestTenantContext.Default, tenantExecutionContext: TestTenantContext.Default);
    }

    private static void SeedDelivery(ApplicationDbContext db)
    {
        var branch = new Branch { Id = 1, Name = "Sucursal" };
        db.Branches.Add(branch);
        db.BranchPrintSettings.Add(
            new BranchPrintSettings
            {
                BranchId = 1,
                Branch = branch,
                EnableDeliveryJobs = true,
            });
        db.Orders.Add(
            new Order
            {
                Id = 10,
                BranchId = 1,
                TakenById = 1,
                Type = OrderType.Delivery,
                Status = OrderStatus.OnTheWay,
                DeliveryManId = 5,
                Total = 20000,
                CreatedAt = DateTime.UtcNow,
                Branch = branch,
            });
        db.SaveChanges();
    }

    private static PrintQueueService CreateService(
        ApplicationDbContext db,
        IPrintAgentNotifier notifier)
    {
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(DateTime.UtcNow);
        return new PrintQueueService(
            db,
            Options.Create(new ApiPublicOptions()),
            Options.Create(new BrandingOptions()),
            clock.Object,
            notifier,
            TestTenantContext.Default,
            NullLogger<PrintQueueService>.Instance);
    }

    private sealed class RecordingNotifier : IPrintAgentNotifier
    {
        private readonly ApplicationDbContext _db;

        public RecordingNotifier(ApplicationDbContext db)
        {
            _db = db;
        }

        public long JobId { get; private set; }
        public bool JobWasPersistedWhenCalled { get; private set; }

        public async Task NotifyJobsAvailableAsync(
            int branchId,
            long jobId,
            PrintJobKind kind,
            CancellationToken cancellationToken = default)
        {
            JobId = jobId;
            JobWasPersistedWhenCalled = await _db.PrintJobs
                .AnyAsync(x => x.Id == jobId, cancellationToken);
        }
    }

    private sealed class ThrowingNotifier : IPrintAgentNotifier
    {
        public Task NotifyJobsAvailableAsync(
            int branchId,
            long jobId,
            PrintJobKind kind,
            CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("SignalR unavailable");
    }
}
