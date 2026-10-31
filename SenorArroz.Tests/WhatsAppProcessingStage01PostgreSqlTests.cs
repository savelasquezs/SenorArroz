using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SenorArroz.API.Controllers;
using SenorArroz.API.Hubs;
using SenorArroz.API.Services;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Models;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Services;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Services;

namespace SenorArroz.Tests;

public sealed class PostgreSqlIntegrationTheoryAttribute : TheoryAttribute
{
    public PostgreSqlIntegrationTheoryAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("RUN_POSTGRES_INTEGRATION_TESTS"), "true", StringComparison.OrdinalIgnoreCase))
            Skip = "Requiere RUN_POSTGRES_INTEGRATION_TESTS=true y Docker.";
    }
}

public sealed partial class WhatsAppProcessingPostgreSqlTests
{
    [PostgreSqlIntegrationTheory]
    [Trait("Category", "PostgreSqlIntegration")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Incoming_and_manual_work_share_conversation_order_and_exclusion(bool incomingFirst)
    {
        using var tenant = _tenant.BeginTenantScope(1);
        await using var db = Db();
        await SeedManualAsync(db);
        var (queue, _, _, http) = ManualQueue(db);
        Task Incoming() => Store(db).EnqueueAsync(1, WhatsAppProcessingTests.Payload("wamid.mixed").Replace("channel-1", "central"), default);
        async Task Manual() => await queue.EnqueueAsync<WhatsAppMessageDto>("send-message", 11,
            JsonSerializer.SerializeToElement(new { text = "En orden" }), http.HttpContext!.Request, default);
        if (incomingFirst) { await Incoming(); await Manual(); } else { await Manual(); await Incoming(); }
        var rows = await db.WhatsAppWebhookEvents.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(rows[0].PartitionKey, rows[1].PartitionKey);
        var first = (await Store(db).ClaimAsync(default))!;
        Assert.Equal(rows[0].Id, first.Id);
        Assert.Null(await Store(db).ClaimAsync(default));
        await Store(db).FinishAsync(first, true, default);
        Assert.Equal(rows[1].Id, (await Store(db).ClaimAsync(default))!.Id);
    }

    [PostgreSqlIntegrationTheory]
    [Trait("Category", "PostgreSqlIntegration")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Uncertain_manual_transport_requires_review_and_does_not_send_a_menu_fallback(bool menu)
    {
        using var tenant = _tenant.BeginTenantScope(1);
        await using var db = Db();
        await SeedManualAsync(db);
        var (queue, cloud, _, http) = ManualQueue(db);
        cloud.Setup(x => x.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WhatsAppCloudSendResult(false, null, "Meta WhatsApp timeout: response lost"));
        cloud.Setup(x => x.SendImageLinkMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WhatsAppCloudSendResult(false, null, "Meta WhatsApp network_error: response lost"));
        if (menu) await db.Branches.Where(x => x.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(x => x.MenuImageUrl1, "https://example.invalid/menu.jpg"));
        await queue.EnqueueAsync<WhatsAppMessageDto>(menu ? "send-menu" : "send-message", 11,
            JsonSerializer.SerializeToElement(new { text = "No repetir" }), http.HttpContext!.Request, default);
        var job = (await Store(db).ClaimAsync(default))!;
        await Assert.ThrowsAsync<IOException>(() => ExecuteManualWorkerAsync(queue, job, db));
        Assert.True(await Store(db).FinishAsync(job, false, default));
        Assert.Null(await Store(db).ClaimAsync(default));
        Assert.Equal(409, Assert.IsType<ConflictObjectResult>(await queue.GetResultAsync(job.Id, default)).StatusCode);
        Assert.Empty(await db.WhatsAppMessages.ToListAsync());
        cloud.Verify(x => x.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), menu ? Times.Never() : Times.Once());
        cloud.Verify(x => x.SendImageLinkMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), menu ? Times.Once() : Times.Never());
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Superadmin_without_selected_branch_keeps_server_assigned_work_origin()
    {
        using var tenant = _tenant.BeginTenantScope(1);
        await using var db = Db();
        await SeedManualAsync(db);
        await db.Users.ExecuteUpdateAsync(x => x.SetProperty(u => u.Role, UserRole.Superadmin));
        var (queue, _, _, http) = ManualQueue(db, "superadmin");
        var accepted = await queue.EnqueueAsync<WhatsAppMessageDto>("send-message", 11,
            JsonSerializer.SerializeToElement(new { text = "Global" }), http.HttpContext!.Request, default);
        Assert.Equal(202, Assert.IsType<ObjectResult>(accepted.Result).StatusCode);
        var job = (await Store(db).ClaimAsync(default))!;
        var command = JsonSerializer.Deserialize<WhatsAppManualWorkQueue.Command>(Store(db).ReadPayload(job), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(1, command.WorkBranchId);
        Assert.Null(http.HttpContext.Request.Headers["X-Branch-Id"].FirstOrDefault());
        await ExecuteManualWorkerAsync(queue, job, db);
        Assert.True(await Store(db).FinishAsync(job, true, default));
        Assert.Equal(200, Assert.IsType<ObjectResult>(await queue.GetResultAsync(job.Id, default)).StatusCode);
        Assert.Equal(2, (await db.Orders.AsNoTracking().SingleAsync()).BranchId);
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Ai_consumers_run_different_chats_concurrently_and_continue_the_same_chat_in_order_without_recovery_polling()
    {
        using (_tenant.BeginTenantScope(1))
        {
            await using var seed = Db();
            await SeedManualAsync(seed);
            seed.WhatsAppConversations.Add(new WhatsAppConversation { Id = 12, BranchId = 1,
                PhoneNumber = "573000000002", AttentionModeUpdatedAt = _clock.UtcNow });
            seed.WhatsAppMessages.AddRange(Enumerable.Range(1, 3).Select(id => new WhatsAppMessage
            {
                Id = id, ConversationId = id == 3 ? 12 : 11, WhatsAppMessageId = $"wamid.ai-{id}",
                Direction = WhatsAppMessageDirection.Inbound, TextBody = "test", Timestamp = _clock.UtcNow,
                Status = WhatsAppMessageStatus.Received, AiProcessingStatus = WhatsAppAiProcessingStatus.Pending
            }));
            await seed.SaveChangesAsync();
        }
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var otherStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observedConcurrency = new System.Collections.Concurrent.ConcurrentBag<int>();
        var active = 0;
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentTenant>(_tenant);
        services.AddSingleton<ITenantExecutionContext>(_tenant);
        services.AddDbContext<ApplicationDbContext>(x => x.UseNpgsql(_postgres.GetConnectionString()));
        services.AddScoped<IWhatsAppAiOrchestrator>(sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var claimer = new WhatsAppAiMessageClaimer(db, Options.Create(new WhatsAppAiOrchestratorOptions()));
            var orchestrator = new Mock<IWhatsAppAiOrchestrator>();
            orchestrator.Setup(x => x.ProcessIncomingMessageAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns(async (int conversationId, int messageId, CancellationToken ct) =>
                {
                    if (!await claimer.TryClaimAsync(conversationId, messageId, ct))
                        return new WhatsAppAiProcessingResult(false, true, null, false, false, null, null, 0, 0, null);
                    observedConcurrency.Add(Interlocked.Increment(ref active));
                    try
                    {
                        if (messageId == 1) { firstStarted.TrySetResult(); await release.Task.WaitAsync(ct); }
                        if (messageId == 3) otherStarted.TrySetResult();
                        await db.WhatsAppMessages.Where(x => x.Id == messageId).ExecuteUpdateAsync(s => s
                            .SetProperty(x => x.AiProcessingStatus, WhatsAppAiProcessingStatus.Completed), ct);
                        if (messageId == 2) secondStarted.TrySetResult();
                        return new WhatsAppAiProcessingResult(true, false, null, false, false, null, null, 0, 0, null);
                    }
                    finally { Interlocked.Decrement(ref active); }
                });
            return orchestrator.Object;
        });
        using var provider = services.BuildServiceProvider();
        var queue = new WhatsAppAiWorkQueue();
        foreach (var id in new[] { 1, 2, 3 }) Assert.True(queue.TryEnqueue(id == 3 ? 12 : 11, id));
        using var background = new WhatsAppAiBackgroundService(queue, provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WhatsAppAiBackgroundService>.Instance, Options.Create(new WhatsAppAiOrchestratorOptions()),
            Options.Create(new WhatsAppProcessingOptions { Mode = "External", AiConcurrency = 2 }));
        await background.StartAsync(default);
        try
        {
            await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await otherStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(secondStarted.Task.IsCompleted);
            Assert.Equal(2, observedConcurrency.Max());
            release.TrySetResult();
            await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            release.TrySetResult();
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await background.StopAsync(stop.Token);
        }
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Queue_benchmark_records_reproducible_latency_backlog_connections_and_sql_commands()
    {
        const int messages = 240;
        const int producers = 8;
        const int consumers = 4;
        long commands = 0;
        var latencies = new System.Collections.Concurrent.ConcurrentBag<double>();
        var processing = new System.Collections.Concurrent.ConcurrentBag<double>();
        var benchmarkOptions = new DbContextOptionsBuilder<ApplicationDbContext>(_dbOptions)
            .LogTo(_ => Interlocked.Increment(ref commands), new[] { Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.CommandExecuted }).Options;
        ApplicationDbContext BenchDb() => new(benchmarkOptions, currentTenant: _tenant, tenantExecutionContext: _tenant);
        using (_tenant.BeginTenantScope(1))
        {
            await using var seed = BenchDb();
            await SeedManualAsync(seed);
        }
        // Warm up model, connection pool, encryption and the two queue query paths.
        await using (var warm = BenchDb())
        {
            await Store(warm).EnqueueAsync(1, WhatsAppProcessingTests.Payload("warm"), default);
            await Store(warm).FinishAsync((await Store(warm).ClaimAsync(default))!, true, default);
        }
        commands = 0;
        using var process = Process.GetCurrentProcess();
        var cpuBefore = process.TotalProcessorTime;
        var elapsed = Stopwatch.StartNew();
        var payloadBytes = System.Text.Encoding.UTF8.GetByteCount(WhatsAppProcessingTests.Payload());
        await Parallel.ForEachAsync(Enumerable.Range(0, messages), new ParallelOptions { MaxDegreeOfParallelism = producers }, async (index, ct) =>
        {
            await using var db = BenchDb();
            var watch = Stopwatch.StartNew();
            await Store(db).EnqueueAsync(index % 2 + 1, WhatsAppProcessingTests.Payload($"benchmark-{index}", $"57300000{index % 32:D4}"), ct);
            latencies.Add(watch.Elapsed.TotalMilliseconds);
        });
        var ingressSeconds = elapsed.Elapsed.TotalSeconds;
        int backlog;
        int connections;
        using (_tenant.BeginSystemScope())
        {
            await using var measure = BenchDb();
            backlog = (int)(await WhatsAppProcessingMaintenance.ReadAsync(measure, DateTime.UtcNow, default)).Pending;
            connections = await measure.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE datname = current_database()").SingleAsync();
        }
        var drain = Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(0, consumers).Select(async _ =>
        {
            await using var db = BenchDb();
            while (await Store(db).ClaimAsync(default) is { } job)
            {
                var watch = Stopwatch.StartNew();
                await Task.Delay(10);
                Assert.True(await Store(db).FinishAsync(job, true, default));
                processing.Add(watch.Elapsed.TotalMilliseconds);
            }
        }));
        Assert.Equal(messages, processing.Count);
        Assert.Equal(messages, backlog);
        drain.Stop();
        var relayLatency = new System.Collections.Concurrent.ConcurrentBag<double>();
        var allowed = TenantRealtimeGroups.BranchRole(1, 1, "WhatsApp");
        using (_tenant.BeginTenantScope(1))
        {
            await using var seed = BenchDb();
            seed.WhatsAppRealtimeEvents.AddRange(Enumerable.Range(1, 32).Select(_ => new WhatsAppRealtimeEvent
            { ConversationId = 11, EventName = "WhatsAppMessageCreated", Payload = "{\"conversation\":{\"id\":11}}", Groups = [allowed] }));
            await seed.SaveChangesAsync();
        }
        var relayWatch = Stopwatch.StartNew();
        var proxy = new Mock<IClientProxy>();
        proxy.Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Callback(() => relayLatency.Add(relayWatch.Elapsed.TotalMilliseconds)).Returns(Task.CompletedTask);
        var clients = new Mock<IHubClients>();
        clients.Setup(x => x.Groups(It.IsAny<IReadOnlyList<string>>())).Returns(proxy.Object);
        var hub = new Mock<IHubContext<WhatsAppHub>>();
        hub.SetupGet(x => x.Clients).Returns(clients.Object);
        var services = new ServiceCollection();
        services.AddSingleton<ITenantExecutionContext>(_tenant);
        services.AddScoped(_ => BenchDb());
        using var provider = services.BuildServiceProvider();
        var relay = new WhatsAppRealtimeRelay(provider.GetRequiredService<IServiceScopeFactory>(), hub.Object,
            Options.Create(new WhatsAppAiOrchestratorOptions()), NullLogger<WhatsAppRealtimeRelay>.Instance);
        Assert.Equal(32, await relay.RelayOnceAsync(default));
        Assert.Equal(32, relayLatency.Count);
        process.Refresh();
        var result = new
        {
            capturedAtUtc = DateTime.UtcNow, profile = "Disposable PostgreSQL 16, durable queue only; no Meta/LLM, no production traffic, simulated handler 10 ms",
            framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            operatingSystem = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            messages, producers, consumers, conversations = 32, tenants = 2, payloadBytes,
            ingress = new { p50Ms = Percentile(latencies, .50), p95Ms = Percentile(latencies, .95), seconds = ingressSeconds, perSecond = messages / ingressSeconds },
            processing = new { p50Ms = Percentile(processing, .50), p95Ms = Percentile(processing, .95), drainSeconds = drain.Elapsed.TotalSeconds },
            relay = new { events = 32, p50Ms = Percentile(relayLatency, .50), p95Ms = Percentile(relayLatency, .95), transport = "mock SignalR proxy; client network excluded" },
            peakQueuedAtDrain = backlog, postgresConnectionsAfterIngress = connections, sqlCommandCount = commands,
            testProcessCpuMs = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds,
            testProcessWorkingSetBytes = process.WorkingSet64,
            budgets = new { ingressP95Ms = 500, metIngressBudget = Percentile(latencies, .95) <= 500,
                relayP95Ms = 1500, metRelayBudget = Percentile(relayLatency, .95) <= 1500 }
        };
        var directory = Path.Combine(RepositoryRoot(), "artifacts", "whatsapp-stage01");
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(directory, "queue-benchmark.json"), json);
        output.WriteLine(json);
    }

    private static double Percentile(IEnumerable<double> samples, double percentile)
    {
        var ordered = samples.OrderBy(x => x).ToArray();
        return Math.Round(ordered[(int)Math.Ceiling(ordered.Length * percentile) - 1], 2);
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Manual_send_is_durable_idempotent_and_completed_result_survives_a_crash()
    {
        using var tenant = _tenant.BeginTenantScope(1);
        await using var db = Db();
        await SeedManualAsync(db);
        var (queue, cloud, _, http) = ManualQueue(db);
        var body = JsonSerializer.SerializeToElement(new { text = "Pedido para otra sede" });
        var first = await queue.EnqueueAsync<WhatsAppMessageDto>("send-message", 11, body, http.HttpContext!.Request, default);
        Assert.Equal(202, Assert.IsType<ObjectResult>(first.Result).StatusCode);
        await queue.EnqueueAsync<WhatsAppMessageDto>("send-message", 11, body, http.HttpContext.Request, default);
        var conflicting = await queue.EnqueueAsync<WhatsAppMessageDto>("send-message", 11,
            JsonSerializer.SerializeToElement(new { text = "Otra solicitud" }), http.HttpContext.Request, default);
        Assert.IsType<ConflictObjectResult>(conflicting.Result);
        Assert.Single(await db.WhatsAppWebhookEvents.ToListAsync());
        cloud.VerifyNoOtherCalls();
        var job = (await Store(db).ClaimAsync(default))!;
        await ExecuteManualWorkerAsync(queue, job, db);
        Assert.Single(await db.WhatsAppMessages.Where(x => x.Direction == WhatsAppMessageDirection.Outbound).ToListAsync());
        // The response is persisted; simulate a crash before FinishAsync.
        _clock.UtcNow = _clock.UtcNow.AddMinutes(4);
        Assert.Null(await Store(db).ClaimAsync(default));
        var result = Assert.IsType<ObjectResult>(await queue.GetResultAsync(job.Id, default));
        Assert.Equal(200, result.StatusCode);
        var responseProfile = new
        {
            scope = "Serialized response bodies in disposable integration; HTTP headers excluded",
            acceptedBytes = JsonSerializer.SerializeToUtf8Bytes(Assert.IsType<ObjectResult>(first.Result).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)).Length,
            completedBytes = JsonSerializer.SerializeToUtf8Bytes(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)).Length
        };
        var artifacts = Path.Combine(RepositoryRoot(), "artifacts", "whatsapp-stage01");
        Directory.CreateDirectory(artifacts);
        await File.WriteAllTextAsync(Path.Combine(artifacts, "manual-response-profile.json"), JsonSerializer.Serialize(responseProfile, new JsonSerializerOptions { WriteIndented = true }));
        Assert.Equal("completed", (await db.WhatsAppWebhookEvents.AsNoTracking().SingleAsync()).ProcessingState);
        Assert.False(await Store(db).FinishAsync(job, true, default));
        await queue.EnqueueAsync<WhatsAppMessageDto>("send-message", 11, body, http.HttpContext.Request, default);
        Assert.Null(await Store(db).ClaimAsync(default));
        cloud.Verify(x => x.SendTextMessageAsync("central", "local-token", "573000000001", "Pedido para otra sede", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(1, (await db.WhatsAppConversations.AsNoTracking().SingleAsync()).OperationalBranchId);
        var remoteOrder = await db.Orders.AsNoTracking().SingleAsync();
        Assert.Equal(1, remoteOrder.OriginBranchId);
        Assert.Equal(2, remoteOrder.BranchId);
        Assert.Equal(7, remoteOrder.TakenById);
        using (_tenant.BeginTenantScope(2))
            Assert.IsType<NotFoundResult>(await queue.GetResultAsync(job.Id, default));
    }

    [PostgreSqlIntegrationTheory]
    [Trait("Category", "PostgreSqlIntegration")]
    [InlineData("attention", 409)]
    [InlineData("branch", 403)]
    [InlineData("role", 403)]
    public async Task Manual_worker_revalidates_attention_actor_and_branch(string change, int expectedStatus)
    {
        using var tenant = _tenant.BeginTenantScope(1);
        await using var db = Db();
        await SeedManualAsync(db);
        var (queue, cloud, _, http) = ManualQueue(db);
        await queue.EnqueueAsync<WhatsAppMessageDto>("send-message", 11,
            JsonSerializer.SerializeToElement(new { text = "No enviar" }), http.HttpContext!.Request, default);
        if (change == "attention")
            await db.WhatsAppConversations.ExecuteUpdateAsync(s => s.SetProperty(x => x.AttentionModeUpdatedAt, _clock.UtcNow.AddMinutes(1)));
        else if (change == "branch")
            await db.Users.ExecuteUpdateAsync(s => s.SetProperty(x => x.BranchId, 2));
        else await db.Users.ExecuteUpdateAsync(s => s.SetProperty(x => x.Role, UserRole.Kitchen));
        var job = (await Store(db).ClaimAsync(default))!;
        await ExecuteManualWorkerAsync(queue, job, db);
        Assert.True(await Store(db).FinishAsync(job, true, default));
        var persisted = await db.WhatsAppWebhookEvents.AsNoTracking().SingleAsync();
        var response = JsonSerializer.Deserialize<WhatsAppManualWorkQueue.Result>(_protector.Unprotect(persisted.ProtectedResult!), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Empty(await db.WhatsAppMessages.ToListAsync());
        cloud.VerifyNoOtherCalls();
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Interrupted_manual_send_requires_review_and_is_never_automatically_reposted()
    {
        using var tenant = _tenant.BeginTenantScope(1);
        await using var db = Db();
        await SeedManualAsync(db);
        var (queue, cloud, _, http) = ManualQueue(db);
        await queue.EnqueueAsync<WhatsAppMessageDto>("send-message", 11,
            JsonSerializer.SerializeToElement(new { text = "Incierto" }), http.HttpContext!.Request, default);
        Assert.NotNull(await Store(db).ClaimAsync(default));
        _clock.UtcNow = _clock.UtcNow.AddMinutes(4);
        Assert.Null(await Store(db).ClaimAsync(default));
        Assert.Equal(409, Assert.IsType<ConflictObjectResult>(await queue.GetResultAsync((await db.WhatsAppWebhookEvents.SingleAsync()).Id, default)).StatusCode);
        cloud.VerifyNoOtherCalls();
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Manual_media_uses_durable_storage_and_never_places_binary_content_in_postgres()
    {
        using var tenant = _tenant.BeginTenantScope(1);
        await using var db = Db();
        await SeedManualAsync(db);
        var (queue, cloud, storage, http) = ManualQueue(db);
        var bytes = new byte[] { 0, 1, 2, 3 };
        storage.Setup(x => x.UploadPublicObjectAsync(It.IsAny<byte[]>(), It.IsAny<string>(), "audio/ogg", It.IsAny<CancellationToken>())).ReturnsAsync("https://example.invalid/staged.ogg");
        storage.Setup(x => x.DownloadObjectAsync(It.Is<string>(s => s.StartsWith("whatsapp/1/manual/11/")), 69_000_000, It.IsAny<CancellationToken>())).ReturnsAsync(bytes);
        cloud.Setup(x => x.UploadMediaAsync("central", "local-token", It.IsAny<byte[]>(), "audio.ogg", "audio/ogg", It.IsAny<CancellationToken>())).ReturnsAsync(new WhatsAppCloudUploadMediaResult(true, "media-id", null));
        cloud.Setup(x => x.SendMediaMessageAsync("central", "local-token", "573000000001", "audio", "media-id", null, "audio.ogg", It.IsAny<CancellationToken>())).ReturnsAsync(new WhatsAppCloudSendResult(true, "wamid.audio", null));
        using var stream = new MemoryStream(bytes);
        var file = new FormFile(stream, 0, bytes.Length, "file", "audio.ogg") { Headers = new HeaderDictionary(), ContentType = "audio/ogg" };
        await queue.EnqueueMediaAsync(11, file, null, http.HttpContext!.Request, default);
        var job = (await Store(db).ClaimAsync(default))!;
        Assert.DoesNotContain(Convert.ToBase64String(bytes), Store(db).ReadPayload(job));
        await ExecuteManualWorkerAsync(queue, job, db);
        await Store(db).FinishAsync(job, true, default);
        Assert.Equal(200, Assert.IsType<ObjectResult>(await queue.GetResultAsync(job.Id, default)).StatusCode);
        Assert.Equal("https://example.invalid/staged.ogg", (await db.WhatsAppMessages.SingleAsync()).MediaUrl);
        storage.Verify(x => x.UploadPublicObjectAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        http.HttpContext.Request.Headers["X-WhatsApp-Request-Id"] = Guid.NewGuid().ToString();
        storage.Setup(x => x.UploadPublicObjectAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Storage unavailable"));
        var failedStage = await queue.EnqueueMediaAsync(11, file, null, http.HttpContext.Request, default);
        Assert.IsType<BadRequestObjectResult>(failedStage.Result);
        Assert.Single(await db.WhatsAppWebhookEvents.ToListAsync());
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Phone_and_bsuid_aliases_share_order_without_merging_other_lines_or_tenants()
    {
        await using var db = Db();
        var phone = WhatsAppProcessingTests.Payload("phone-first");
        var bridge = WhatsAppProcessingTests.Payload("bridge").Replace("\"from\":\"573000000001\"", "\"from\":\"573000000001\",\"from_user_id\":\"user.one\"");
        var bsuid = WhatsAppProcessingTests.Payload("bsuid-third").Replace("\"from\":\"573000000001\"", "\"from_user_id\":\"user.one\"");
        await Store(db).EnqueueAsync(1, phone, default);
        await Store(db).EnqueueAsync(1, bridge, default);
        await Store(db).EnqueueAsync(1, bsuid, default);
        await Store(db).EnqueueAsync(1, bsuid.Replace("channel-1", "channel-2"), default);
        await Store(db).EnqueueAsync(2, phone, default);
        using (_tenant.BeginTenantScope(1))
        {
            var same = await db.WhatsAppWebhookEvents.AsNoTracking().OrderBy(x => x.Id).Take(3).ToListAsync();
            Assert.Single(same.Select(x => x.PartitionKey).Distinct());
            Assert.Equal(2, same[^1].PartitionAliases.Length);
        }
        var jobs = new[] { await Store(db).ClaimAsync(default), await Store(db).ClaimAsync(default), await Store(db).ClaimAsync(default) };
        Assert.All(jobs, x => Assert.NotNull(x));
        Assert.Null(await Store(db).ClaimAsync(default));
        foreach (var job in jobs) await Store(db).FinishAsync(job!, true, default);
        Assert.NotNull(await Store(db).ClaimAsync(default));
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Retention_is_bounded_keeps_deduplication_and_never_removes_pending_work()
    {
        await using var db = Db();
        await Store(db).EnqueueAsync(1, WhatsAppProcessingTests.Payload("old-completed"), default);
        var first = (await Store(db).ClaimAsync(default))!;
        await Store(db).FinishAsync(first, true, default);
        await Store(db).EnqueueAsync(1, WhatsAppProcessingTests.Payload("pending"), default);
        using var tenant = _tenant.BeginTenantScope(1);
        db.WhatsAppRealtimeEvents.AddRange(Enumerable.Range(1, 4).Select(x => new WhatsAppRealtimeEvent { ConversationId = x, EventName = "test", Groups = [], Payload = "{}" }));
        await db.SaveChangesAsync();
        await db.WhatsAppWebhookEvents.Where(x => x.Id == first.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.UpdatedAt, _clock.UtcNow.AddDays(-10)));
        await db.WhatsAppRealtimeEvents.ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, _clock.UtcNow.AddDays(-10)));
        var before = await WhatsAppProcessingMaintenance.ReadAsync(db, _clock.UtcNow, default);
        Assert.Equal(1, before.Pending);
        Assert.Equal(4, before.RealtimeEvents);
        await WhatsAppProcessingMaintenance.PruneAsync(db, _clock.UtcNow, new WhatsAppProcessingOptions { CleanupBatchSize = 2 }, default);
        Assert.Equal(2, await db.WhatsAppRealtimeEvents.CountAsync());
        Assert.Equal("{}", (await db.WhatsAppWebhookEvents.AsNoTracking().SingleAsync(x => x.Id == first.Id)).RawPayload);
        await Store(db).EnqueueAsync(1, WhatsAppProcessingTests.Payload("old-completed"), default);
        Assert.Equal(2, await db.WhatsAppWebhookEvents.CountAsync());
        Assert.Equal(1, (await WhatsAppProcessingMaintenance.ReadAsync(db, _clock.UtcNow, default)).Pending);
    }

    private async Task ExecuteManualWorkerAsync(WhatsAppManualWorkQueue queue, WhatsAppWebhookEvent job, ApplicationDbContext db)
    {
        var payload = Store(db).ReadPayload(job);
        Task work;
        using (ExecutionContext.SuppressFlow())
            work = Task.Run(async () =>
            {
                using var tenant = _tenant.BeginTenantScope(job.TenantId);
                await queue.ExecuteAsync(job, payload, default);
            });
        await work;
    }

    private async Task SeedManualAsync(ApplicationDbContext db)
    {
        db.Branches.AddRange(new Branch { Id = 1, Name = "Santander", Address = "-", Phone1 = "3000000000" },
            new Branch { Id = 2, Name = "Preparación remota", Address = "-", Phone1 = "3000000002" });
        db.Users.Add(new User { Id = 7, BranchId = 1, Role = UserRole.Cashier, Name = "Asesor", Email = "worker@example.invalid" });
        db.WhatsAppChannelSettings.Add(new WhatsAppChannelSetting { Id = 1, PhoneNumberId = "central", AccessToken = "local-token", IsActive = true, IsVerified = true });
        db.WhatsAppConversations.Add(new WhatsAppConversation { Id = 11, BranchId = 1, OperationalBranchId = 1,
            ChannelSettingId = 1, AssignedUserId = 7, PhoneNumber = "573000000001", AttentionMode = WhatsAppAttentionMode.Human,
            AttentionModeUpdatedAt = _clock.UtcNow });
        db.Orders.Add(new Order { Id = 91, BranchId = 2, OriginBranchId = 1, TakenById = 7,
            WhatsAppConversationId = 11, Type = OrderType.Onsite, Status = OrderStatus.Taken });
        await db.SaveChangesAsync();
    }

    private (WhatsAppManualWorkQueue Queue, Mock<IWhatsAppCloudClient> Cloud, Mock<IFirebaseGcsStorage> Storage, IHttpContextAccessor Http) ManualQueue(ApplicationDbContext db, string role = "cashier")
    {
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        http.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "7"),
            new Claim(ClaimTypes.Role, role), new Claim("branch_id", "1"), new Claim("tenant_id", "1") }, "test"));
        http.HttpContext.Request.Scheme = "https";
        http.HttpContext.Request.Host = new HostString("test.invalid");
        http.HttpContext.Request.Headers["X-WhatsApp-Request-Id"] = Guid.NewGuid().ToString();
        var current = new CurrentUserService(http);
        var branch = new BranchContextService(http, current, db, _tenant);
        var cloud = new Mock<IWhatsAppCloudClient>();
        cloud.Setup(x => x.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WhatsAppCloudSendResult(true, "wamid.manual", null));
        var notifications = Mock.Of<IWhatsAppNotificationService>();
        var storage = new Mock<IFirebaseGcsStorage>();
        var attention = new WhatsAppAttentionService();
        var processor = new WhatsAppWebhookProcessor(db, _clock, cloud.Object, notifications, storage.Object,
            Options.Create(new FirebaseStorageOptions()), Options.Create(new WhatsAppAiOrchestratorOptions()),
            NullLogger<WhatsAppWebhookProcessor>.Instance, attention, new WhatsAppAiWorkQueue(),
            Mock.Of<IWhatsAppAutomaticMessageSender>(), Mock.Of<IBranchBusinessHoursService>(), null!, null!, _tenant);
        var operations = new WhatsAppManualOperations(db, current, branch, _clock, cloud.Object, notifications,
            Options.Create(new WhatsAppCloudOptions()), NullLogger<WhatsAppController>.Instance, attention, null!, processor, _tenant,
            processing: Options.Create(new WhatsAppProcessingOptions { Mode = "External" }));
        return (new WhatsAppManualWorkQueue(db, current, branch, _tenant, _protector, _clock, operations,
            storage.Object, http, NullLogger<WhatsAppManualWorkQueue>.Instance, Store(db)), cloud, storage, http);
    }
}
