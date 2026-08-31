using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Services;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests;

public class KitchenAutoPrintServiceTests
{
    [Fact]
    public async Task Matching_trigger_enqueues_automatic_kitchen_job()
    {
        await using var db = CreateDb(KitchenAutoPrintTrigger.WhenOrderCreated);
        var queue = new Mock<IPrintQueueService>();
        queue.Setup(x => x.EnqueueAutomaticKitchenAsync(
                1,
                10,
                KitchenAutoPrintTrigger.WhenOrderCreated,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PrintJob { Id = 1 });
        var service = CreateService(db, queue.Object);

        var enqueued = await service.TryEnqueueAsync(
            new Order { Id = 10, BranchId = 1 },
            KitchenAutoPrintTrigger.WhenOrderCreated);

        Assert.True(enqueued);
        queue.VerifyAll();
    }

    [Fact]
    public async Task Different_trigger_does_not_enqueue()
    {
        await using var db = CreateDb(KitchenAutoPrintTrigger.WhenMarkedReady);
        var queue = new Mock<IPrintQueueService>(MockBehavior.Strict);
        var service = CreateService(db, queue.Object);

        var enqueued = await service.TryEnqueueAsync(
            new Order { Id = 10, BranchId = 1 },
            KitchenAutoPrintTrigger.WhenOrderCreated);

        Assert.False(enqueued);
        queue.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Queue_failure_does_not_escape_to_order_flow()
    {
        await using var db = CreateDb(KitchenAutoPrintTrigger.WhenOrderCreated);
        var queue = new Mock<IPrintQueueService>();
        queue.Setup(x => x.EnqueueAutomaticKitchenAsync(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<KitchenAutoPrintTrigger>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("printer unavailable"));
        var service = CreateService(db, queue.Object);

        var enqueued = await service.TryEnqueueAsync(
            new Order { Id = 10, BranchId = 1 },
            KitchenAutoPrintTrigger.WhenOrderCreated);

        Assert.False(enqueued);
    }

    private static ApplicationDbContext CreateDb(KitchenAutoPrintTrigger trigger)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ApplicationDbContext(options);
        db.BranchPrintSettings.Add(new BranchPrintSettings
        {
            BranchId = 1,
            EnableKitchenJobs = true,
            KitchenAutoPrintTrigger = trigger,
        });
        db.SaveChanges();
        return db;
    }

    private static KitchenAutoPrintService CreateService(
        ApplicationDbContext db,
        IPrintQueueService queue) =>
        new(db, queue, NullLogger<KitchenAutoPrintService>.Instance);
}
