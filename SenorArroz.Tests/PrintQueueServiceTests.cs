using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Services;

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

    [Fact]
    public async Task Delivery_enqueue_does_not_query_loyalty_repositories()
    {
        await using var db = CreateDb();
        SeedDelivery(db);
        var orderRepository = new Mock<IOrderRepository>(MockBehavior.Strict);
        var loyaltyRepository =
            new Mock<ILoyaltyCycleStepRepository>(MockBehavior.Strict);
        var service = CreateService(
            db,
            new RecordingNotifier(db),
            orderRepository.Object,
            loyaltyRepository.Object);

        await service.EnqueueDeliveryAsync(1, [10]);

        orderRepository.VerifyNoOtherCalls();
        loyaltyRepository.VerifyNoOtherCalls();
    }

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
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
        IPrintAgentNotifier notifier,
        IOrderRepository? orderRepository = null,
        ILoyaltyCycleStepRepository? loyaltyRepository = null)
    {
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(DateTime.UtcNow);
        return new PrintQueueService(
            db,
            Options.Create(new ApiPublicOptions()),
            Options.Create(new BrandingOptions()),
            orderRepository ?? Mock.Of<IOrderRepository>(),
            loyaltyRepository ?? Mock.Of<ILoyaltyCycleStepRepository>(),
            clock.Object,
            notifier,
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
