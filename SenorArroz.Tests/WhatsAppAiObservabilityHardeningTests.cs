using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SenorArroz.API.Controllers;
using SenorArroz.API.Services;
using SenorArroz.Application.Common.Models;
using SenorArroz.Domain.Entities;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests;

public class WhatsAppAiObservabilityHardeningTests
{
    [Theory]
    [InlineData("openai", 100, 0, 20, 0, 100, 0, 20)]
    [InlineData("openai", 100, 25, 20, 10, 75, 25, 20)]
    [InlineData("gemini", 100, 0, 20, 0, 100, 0, 20)]
    [InlineData("gemini", 100, 0, 20, 10, 100, 0, 30)]
    [InlineData("gemini", 100, 25, 20, 10, 75, 25, 30)]
    public void Billing_usage_has_provider_specific_output_semantics(string provider, int input, int cached, int output, int thinking, int expectedUncached, int expectedCached, int expectedBillableOutput)
    {
        var response = new AiChatResponse(null, [], "model", null, input, output, CachedInputTokens: cached, ThinkingTokens: thinking);
        var usage = AiBillingUsage.From(provider, response);
        Assert.Equal(expectedUncached, usage.UncachedInputTokens);
        Assert.Equal(expectedCached, usage.CachedInputTokens);
        Assert.Equal(expectedBillableOutput, usage.BillableOutputTokens);
        Assert.Equal(output, usage.VisibleOutputTokens);
        Assert.Equal(thinking, usage.ThinkingTokens);
    }

    [Fact]
    public void Colombia_midnight_converts_to_five_am_utc()
    {
        Assert.Equal(new DateTime(2026, 7, 13, 5, 0, 0, DateTimeKind.Utc), WhatsAppAiUsageController.ToUtc(new DateOnly(2026, 7, 13)));
    }

    [Fact]
    public void Bounded_queue_rejects_without_waiting_when_full()
    {
        var queue = new WhatsAppAiTelemetryQueue(NullLogger<WhatsAppAiTelemetryQueue>.Instance);
        for (var i = 0; i < 1000; i++) Assert.True(queue.TryEnqueue(new WhatsAppAiInvocation { Provider = "openai", Model = "m" }));
        Assert.False(queue.TryEnqueue(new WhatsAppAiInvocation { Provider = "openai", Model = "m" }));
    }

    [Fact]
    public void Completed_queue_rejects_new_records()
    {
        var queue = NewQueue(); queue.Complete();
        Assert.False(queue.TryEnqueue(Invocation()));
    }

    [Fact]
    public async Task Stop_drains_pending_records_using_independent_context()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var factory = new TestFactory(options); var queue = NewQueue();
        Assert.True(queue.TryEnqueue(Invocation())); Assert.True(queue.TryEnqueue(Invocation()));
        var worker = Worker(queue, factory, 5); await worker.StartAsync(default); await worker.StopAsync(default);
        await using var verification = new ApplicationDbContext(options);
        Assert.Equal(2, await verification.WhatsAppAiInvocations.CountAsync());
        Assert.True(factory.CreatedCount > 0);
    }

    [Fact]
    public async Task Persistence_failure_does_not_prevent_shutdown()
    {
        var queue = NewQueue(); queue.TryEnqueue(Invocation());
        var worker = Worker(queue, new ThrowingFactory(), 2); await worker.StartAsync(default);
        await worker.StopAsync(default);
    }

    [Fact]
    public async Task Shutdown_forces_cancellation_after_timeout()
    {
        var queue = NewQueue(); queue.TryEnqueue(Invocation());
        var worker = Worker(queue, new BlockingFactory(), 1); await worker.StartAsync(default);
        var started = DateTime.UtcNow; await worker.StopAsync(default);
        Assert.InRange((DateTime.UtcNow-started).TotalSeconds, .8, 3);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(10, 9)]
    [InlineData(20, 18)]
    public void P95_position_uses_only_duration_count(int count, int expected) => Assert.Equal(expected, WhatsAppAiUsageController.P95Index(count));

    private static WhatsAppAiTelemetryQueue NewQueue() => new(NullLogger<WhatsAppAiTelemetryQueue>.Instance);
    private static WhatsAppAiInvocation Invocation() => new() { BranchId=1,ConversationId=1,IncomingMessageId=1,Provider="openai",Model="m",StartedAt=DateTime.UtcNow,CreatedAt=DateTime.UtcNow };
    private static WhatsAppAiTelemetryWorker Worker(WhatsAppAiTelemetryQueue queue, IDbContextFactory<ApplicationDbContext> factory, int timeout) => new(queue,factory,Options.Create(new WhatsAppAiTelemetryWorkerOptions{DrainTimeoutSeconds=timeout,BatchSize=50}),NullLogger<WhatsAppAiTelemetryWorker>.Instance);

    private sealed class TestFactory(DbContextOptions<ApplicationDbContext> options) : IDbContextFactory<ApplicationDbContext>
    {
        public int CreatedCount { get; private set; }
        public ApplicationDbContext CreateDbContext() { CreatedCount++; return new(options); }
        public ValueTask<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken=default) => ValueTask.FromResult(CreateDbContext());
    }
    private sealed class ThrowingFactory : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => throw new InvalidOperationException("database unavailable");
        public ValueTask<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken=default) => ValueTask.FromException<ApplicationDbContext>(new InvalidOperationException("database unavailable"));
    }
    private sealed class BlockingFactory : IDbContextFactory<ApplicationDbContext>
    {
        private readonly DbContextOptions<ApplicationDbContext> _options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        public ApplicationDbContext CreateDbContext() => new BlockingDbContext(_options);
        public ValueTask<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken=default) => ValueTask.FromResult(CreateDbContext());
    }
    private sealed class BlockingDbContext(DbContextOptions<ApplicationDbContext> options) : ApplicationDbContext(options)
    {
        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken=default) { await Task.Delay(Timeout.Infinite,cancellationToken); return 0; }
    }
}
