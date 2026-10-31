using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using SenorArroz.API.Services;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Integrations;
using SenorArroz.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Testcontainers.PostgreSql;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SenorArroz.API.Hubs;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace SenorArroz.Tests;

public sealed class WhatsAppProcessingPostgreSqlTests(Xunit.Abstractions.ITestOutputHelper output) : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private DbContextOptions<ApplicationDbContext> _dbOptions = null!;
    private readonly CurrentTenantService _tenant = new(new HttpContextAccessor(), new ConfigurationBuilder().Build());
    private readonly TestClock _clock = new();
    private readonly IIntegrationSecretProtector _protector = new IntegrationSecretProtector(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Integrations:EncryptionKey"] = new string('k', 32) }).Build());

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options;
        await using var db = Db();
        await db.Database.EnsureCreatedAsync();
        using var system = _tenant.BeginSystemScope();
        db.Tenants.AddRange(new Tenant { Id = 1, Name = "One", Slug = "one" }, new Tenant { Id = 2, Name = "Two", Slug = "two" });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();
    private ApplicationDbContext Db() => new(_dbOptions, currentTenant: _tenant, tenantExecutionContext: _tenant);
    private WhatsAppProcessingStore Store(ApplicationDbContext db) => new(db, _tenant, _protector,
        Options.Create(new WhatsAppProcessingOptions { Mode = "External" }), _clock);

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Duplicate_ingress_is_atomic_and_payload_is_encrypted()
    {
        await using var first = Db();
        await using var second = Db();
        await Task.WhenAll(Store(first).EnqueueAsync(1, WhatsAppProcessingTests.Payload(), default),
            Store(second).EnqueueAsync(1, WhatsAppProcessingTests.Payload(), default));
        using var tenant = _tenant.BeginTenantScope(1);
        await using var verify = Db();
        var row = Assert.Single(await verify.WhatsAppWebhookEvents.ToListAsync());
        Assert.DoesNotContain("Hola", row.ProtectedPayload!);
        Assert.Contains("Hola", Store(verify).ReadPayload(row));
        Assert.Equal("pending", row.ProcessingState);
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Concurrent_consumers_preserve_partition_order_and_allow_other_conversations()
    {
        await using var seed = Db();
        await Store(seed).EnqueueAsync(1, WhatsAppProcessingTests.Payload("wamid.order1"), default);
        await Store(seed).EnqueueAsync(1, WhatsAppProcessingTests.Payload("wamid.order2"), default);
        await Store(seed).EnqueueAsync(1, WhatsAppProcessingTests.Payload("wamid.other", "573000000002"), default);
        await using var first = Db();
        await using var second = Db();
        var claimed = await Task.WhenAll(Store(first).ClaimAsync(default), Store(second).ClaimAsync(default));
        Assert.All(claimed, x => Assert.NotNull(x));
        Assert.Equal(2, claimed.Select(x => x!.PartitionKey).Distinct().Count());
        Assert.Null(await Store(seed).ClaimAsync(default));
        Assert.True(await Store(first).FinishAsync(claimed[0]!, true, default));
        Assert.True(await Store(second).FinishAsync(claimed[1]!, true, default));
        Assert.NotNull(await Store(seed).ClaimAsync(default));
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Expired_lease_is_recovered_and_old_owner_cannot_complete_it()
    {
        await using var db = Db();
        await Store(db).EnqueueAsync(1, WhatsAppProcessingTests.Payload("wamid.lease"), default);
        var previous = (await Store(db).ClaimAsync(default))!;
        _clock.UtcNow = _clock.UtcNow.AddMinutes(4);
        var current = (await Store(db).ClaimAsync(default))!;
        Assert.Equal(previous.Id, current.Id);
        Assert.NotEqual(previous.LeaseToken, current.LeaseToken);
        Assert.False(await Store(db).FinishAsync(previous, true, default));
        Assert.True(await Store(db).FinishAsync(current, true, default));
        using var tenant = _tenant.BeginTenantScope(1);
        Assert.Null((await db.WhatsAppWebhookEvents.AsNoTracking().SingleAsync()).ProtectedPayload);
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Tenant_scope_hides_foreign_events_and_uncertain_commits_require_review()
    {
        await using var db = Db();
        await Store(db).EnqueueAsync(2, WhatsAppProcessingTests.Payload("wamid.tenant"), default);
        using (_tenant.BeginTenantScope(1)) Assert.Empty(await db.WhatsAppWebhookEvents.ToListAsync());
        var job = (await Store(db).ClaimAsync(default))!;
        using (_tenant.BeginTenantScope(2))
            await db.WhatsAppWebhookEvents.Where(x => x.Id == job.Id).ExecuteUpdateAsync(x => x.SetProperty(e => e.Processed, true));
        Assert.True(await Store(db).FinishAsync(job, false, default));
        using (_tenant.BeginTenantScope(2))
            Assert.Equal("review", (await db.WhatsAppWebhookEvents.SingleAsync()).ProcessingState);
        Assert.Null(await Store(db).ClaimAsync(default));
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Each_api_replica_receives_events_but_revoked_groups_do_not_receive_content()
    {
        using var tenant = _tenant.BeginTenantScope(1);
        await using var db = Db();
        db.Branches.AddRange(new Branch { Id = 1, Name = "Origin", Address = "-", Phone1 = "3000000000" },
            new Branch { Id = 2, Name = "Destination", Address = "-", Phone1 = "3000000001" });
        db.WhatsAppChannelSettings.Add(new WhatsAppChannelSetting { Id = 1, PhoneNumberId = "central" });
        db.WhatsAppConversations.Add(new WhatsAppConversation { Id = 11, BranchId = 1,
            ChannelSettingId = 1, OperationalBranchId = 2, AttentionModeUpdatedAt = DateTime.UtcNow });
        var allowed = TenantRealtimeGroups.BranchRole(1, 2, "WhatsApp");
        var removed = TenantRealtimeGroups.BranchRole(1, 1, "WhatsApp");
        foreach (var group in new[] { allowed, removed })
            db.WhatsAppRealtimeEvents.Add(new WhatsAppRealtimeEvent { ConversationId = 11,
                EventName = "WhatsAppMessageCreated", Payload = "{\"conversation\":{\"id\":11}}", Groups = [group] });
        await db.SaveChangesAsync();
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentTenant>(_tenant);
        services.AddSingleton<ITenantExecutionContext>(_tenant);
        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(_postgres.GetConnectionString()));
        using var provider = services.BuildServiceProvider();
        var proxy = new Mock<IClientProxy>();
        var clients = new Mock<IHubClients>();
        clients.Setup(x => x.Groups(It.Is<IReadOnlyList<string>>(g => g.Count == 1 && g[0] == allowed))).Returns(proxy.Object);
        var hub = new Mock<IHubContext<WhatsAppHub>>();
        hub.SetupGet(x => x.Clients).Returns(clients.Object);
        var first = new WhatsAppRealtimeRelay(provider.GetRequiredService<IServiceScopeFactory>(), hub.Object,
            Options.Create(new WhatsAppAiOrchestratorOptions()), NullLogger<WhatsAppRealtimeRelay>.Instance);
        var second = new WhatsAppRealtimeRelay(provider.GetRequiredService<IServiceScopeFactory>(), hub.Object,
            Options.Create(new WhatsAppAiOrchestratorOptions()), NullLogger<WhatsAppRealtimeRelay>.Instance);
        Assert.Equal(2, await first.RelayOnceAsync(default));
        Assert.Equal(2, await second.RelayOnceAsync(default));
        Assert.Equal(0, await first.RelayOnceAsync(default));
        proxy.Verify(x => x.SendCoreAsync("WhatsAppMessageCreated", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        clients.Verify(x => x.Groups(It.Is<IReadOnlyList<string>>(g => g.Contains(removed))), Times.Never);
        Assert.All(await db.WhatsAppRealtimeEvents.AsNoTracking().ToListAsync(), e => Assert.Equal(2, e.DeliveredTo.Length));
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Api_accepts_durable_work_without_worker_and_worker_restart_preserves_api_availability()
    {
        using (var tenant = _tenant.BeginTenantScope(1))
        {
            await using var seed = Db();
            seed.Branches.Add(new Branch { Id = 1, Name = "Origin", Address = "-", Phone1 = "3000000000" });
            seed.WhatsAppBranchSettings.Add(new WhatsAppBranchSetting { BranchId = 1,
                PhoneNumberId = "channel-1", BusinessAccountId = "business", DisplayPhoneNumber = "573000000000",
                AccessToken = "local-test", AppSecret = new string('a', 32), WebhookVerifyToken = "local-verify",
                IsActive = true, IsVerified = true });
            await seed.SaveChangesAsync();
        }
        using (var tenant = _tenant.BeginTenantScope(2))
        {
            await using var seed = Db();
            seed.Branches.Add(new Branch { Id = 2, Name = "Tenant two", Address = "-", Phone1 = "3000000002" });
            seed.WhatsAppChannelSettings.Add(new WhatsAppChannelSetting { PhoneNumberId = "channel-2",
                BusinessAccountId = "business-two", AccessToken = "local-test", AppSecret = new string('b', 32),
                IsActive = true, IsVerified = true });
            await seed.SaveChangesAsync();
        }
        var root = RepositoryRoot();
        var configuration = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).Parent!.Name;
        var apiUrl = $"http://127.0.0.1:{FreePort()}";
        var workerUrl = $"http://127.0.0.1:{FreePort()}";
        using var api = Start("SenorArroz.API", apiUrl);
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            await WaitUntil(async () =>
            {
                try { return (await http.GetAsync(apiUrl + "/api/whatsapp/webhook?hub.mode=subscribe&hub.challenge=ok&hub.verify_token=local-verify")).IsSuccessStatusCode; }
                catch (HttpRequestException) { return false; }
            });
            Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsync(apiUrl + "/internal/whatsapp/tools",
                new StringContent("{}", Encoding.UTF8, "application/json"))).StatusCode);
            using (var oversized = new HttpRequestMessage(HttpMethod.Post, apiUrl + "/internal/whatsapp/tools")
            { Content = new StringContent(new string('x', 33000), Encoding.UTF8, "application/json") })
            {
                oversized.Headers.Add("X-WhatsApp-Worker-Key", new string('w', 32));
                Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await http.SendAsync(oversized)).StatusCode);
            }
            var body = WhatsAppProcessingTests.Payload("wamid.process-1");
            using var request = new HttpRequestMessage(HttpMethod.Post, apiUrl + "/api/whatsapp/webhook")
            { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            request.Headers.Add("X-Hub-Signature-256", "sha256=" + Convert.ToHexString(HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(new string('a', 32)), Encoding.UTF8.GetBytes(body))).ToLowerInvariant());
            Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(request)).StatusCode);
            var secondBody = WhatsAppProcessingTests.Payload("wamid.process-tenant2").Replace("channel-1", "channel-2");
            using (var secondRequest = new HttpRequestMessage(HttpMethod.Post, apiUrl + "/api/whatsapp/webhook")
            { Content = new StringContent(secondBody, Encoding.UTF8, "application/json") })
            {
                secondRequest.Headers.Add("X-Hub-Signature-256", "sha256=" + Convert.ToHexString(HMACSHA256.HashData(
                    Encoding.UTF8.GetBytes(new string('b', 32)), Encoding.UTF8.GetBytes(secondBody))).ToLowerInvariant());
                Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(secondRequest)).StatusCode);
            }
            using (_tenant.BeginTenantScope(1))
            {
                await using var before = Db();
                Assert.Empty(await before.WhatsAppMessages.ToListAsync());
                Assert.Single(await before.WhatsAppWebhookEvents.Where(x => x.ProcessingState == "pending").ToListAsync());
            }
            using var worker = Start("SenorArroz.WhatsApp.Worker", workerUrl);
            try
            {
                await WaitUntil(async () =>
                {
                    using var tenant = _tenant.BeginTenantScope(1);
                    await using var verify = Db();
                    return await verify.WhatsAppWebhookEvents.AnyAsync(x => x.ProcessingState == "completed")
                        && await verify.WhatsAppMessages.AnyAsync(x => x.WhatsAppMessageId == "wamid.process-1"
                            && x.AiProcessingStatus == SenorArroz.Domain.Enums.WhatsAppAiProcessingStatus.Ignored);
                });
                await WaitUntil(async () =>
                {
                    using var tenant = _tenant.BeginTenantScope(1);
                    await using var verify = Db();
                    return await verify.WhatsAppRealtimeEvents.AnyAsync(x => x.DeliveredTo.Length > 0);
                });
                await WaitUntil(async () =>
                {
                    using var tenant = _tenant.BeginTenantScope(2);
                    await using var verify = Db();
                    return await verify.WhatsAppMessages.AnyAsync(x => x.WhatsAppMessageId == "wamid.process-tenant2"
                        && x.Conversation.BranchId == 2);
                });
                int sourceId;
                DateTime attentionVersion;
                using (var tenant = _tenant.BeginTenantScope(1))
                {
                    await using var active = Db();
                    await active.WhatsAppConversations.ExecuteUpdateAsync(set => set
                        .SetProperty(x => x.AttentionMode, SenorArroz.Domain.Enums.WhatsAppAttentionMode.Ai)
                        .SetProperty(x => x.AttentionModeUpdatedAt, DateTime.UtcNow));
                    await active.WhatsAppMessages.ExecuteUpdateAsync(set => set
                        .SetProperty(x => x.AiProcessingStatus, SenorArroz.Domain.Enums.WhatsAppAiProcessingStatus.Processing)
                        .SetProperty(x => x.AiProcessingStartedAt, DateTime.UtcNow));
                    sourceId = await active.WhatsAppMessages.Select(x => x.Id).SingleAsync();
                    attentionVersion = await active.WhatsAppConversations.Select(x => x.AttentionModeUpdatedAt).SingleAsync();
                }
                var toolCall = new WhatsAppInternalToolRequest("apply_order_action", sourceId, "rpc-test",
                    JsonSerializer.SerializeToElement(new { action = "clear_cart" }), attentionVersion);
                using (var toolRequest = ToolRequest(toolCall))
                {
                    using var result = await http.SendAsync(toolRequest);
                    Assert.Equal(HttpStatusCode.OK, result.StatusCode);
                    Assert.True((await result.Content.ReadFromJsonAsync<SenorArroz.Application.Common.Models.AgentToolExecutionResult>())!.Success);
                }
                using (var stale = ToolRequest(toolCall with { AttentionVersion = attentionVersion.AddSeconds(-1) }))
                    Assert.Equal(HttpStatusCode.Conflict, (await http.SendAsync(stale)).StatusCode);
                using (var tenant = _tenant.BeginTenantScope(1))
                {
                    await using var active = Db();
                    Assert.NotNull(await active.WhatsAppConversations.Select(x => x.AiOrderState).SingleAsync());
                }
                using (var tenant = _tenant.BeginTenantScope(2))
                {
                    await using var foreign = Db();
                    Assert.Null(await foreign.WhatsAppConversations.Select(x => x.AiOrderState).SingleAsync());
                }
                Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync(workerUrl + "/api/whatsapp/status")).StatusCode);
                Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(workerUrl + "/health/ready")).StatusCode);
            }
            finally { Stop(worker); }
            Assert.False(api.HasExited);
            using var restarted = Start("SenorArroz.WhatsApp.Worker", workerUrl);
            try
            {
                await WaitUntil(async () =>
                {
                    try { return (await http.GetAsync(workerUrl + "/health/ready")).IsSuccessStatusCode; }
                    catch (HttpRequestException) { return false; }
                });
                using var tenant = _tenant.BeginTenantScope(1);
                await using var after = Db();
                Assert.Single(await after.WhatsAppMessages.Where(x => x.WhatsAppMessageId == "wamid.process-1").ToListAsync());
            }
            finally { Stop(restarted); }
        }
        finally { Stop(api); }

        HttpRequestMessage ToolRequest(WhatsAppInternalToolRequest value)
        {
            var result = new HttpRequestMessage(HttpMethod.Post, apiUrl + "/internal/whatsapp/tools")
            { Content = JsonContent.Create(value) };
            result.Headers.Add("X-WhatsApp-Worker-Key", new string('w', 32));
            return result;
        }

        Process Start(string project, string url)
        {
            var dll = Path.Combine(root, project, "bin", configuration, "net9.0", project + ".dll");
            Assert.True(File.Exists(dll), "Build the worker project before running process integration tests.");
            var start = new ProcessStartInfo("dotnet") { WorkingDirectory = Path.GetDirectoryName(dll)!,
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(dll);
            start.ArgumentList.Add("--urls"); start.ArgumentList.Add(url);
            start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            start.Environment["ConnectionStrings__DefaultConnection"] = _postgres.GetConnectionString();
            start.Environment["WhatsAppProcessing__Mode"] = "External";
            start.Environment["WhatsAppProcessing__WorkerKey"] = new string('w', 32);
            start.Environment["WhatsAppProcessing__BusinessApiUrl"] = apiUrl;
            start.Environment["Integrations__EncryptionKey"] = new string('k', 32);
            start.Environment["Logging__LogLevel__Default"] = "Warning";
            start.Environment["WhatsAppAiOrchestrator__RecoveryIntervalSeconds"] = "300";
            start.Environment["Logging__LogLevel__Microsoft"] = "Error";
            start.Environment["Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command"] = "Error";
            var process = Process.Start(start)!;
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.WriteLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) output.WriteLine(e.Data); };
            process.BeginOutputReadLine(); process.BeginErrorReadLine();
            return process;
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SenorArroz.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Migration_is_idempotent_and_realtime_table_enforces_rls_for_runtime_role()
    {
        await using var db = Db();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE SCHEMA IF NOT EXISTS app;
            CREATE OR REPLACE FUNCTION app.current_tenant_id() RETURNS integer LANGUAGE sql STABLE
                AS $$ SELECT NULLIF(current_setting('app.current_tenant_id', true), '')::integer $$;
            CREATE OR REPLACE FUNCTION app.is_system_scope() RETURNS boolean LANGUAGE sql STABLE
                AS $$ SELECT COALESCE(current_setting('app.system_scope', true), 'off') = 'on' $$;
            """);
        var script = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "SenorArroz.Infrastructure", "Scripts", "add_whatsapp_processing_worker.sql"));
        await db.Database.ExecuteSqlRawAsync("""
            DROP TABLE whatsapp_realtime_event;
            ALTER TABLE whatsapp_webhook_event
                DROP COLUMN processing_state, DROP COLUMN deduplication_key, DROP COLUMN partition_key,
                DROP COLUMN protected_payload, DROP COLUMN processing_attempts, DROP COLUMN lease_token,
                DROP COLUMN lease_until, DROP COLUMN next_attempt_at, DROP COLUMN processing_error;
            """);
        await db.Database.ExecuteSqlRawAsync(script);
        await db.Database.ExecuteSqlRawAsync(script);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO whatsapp_realtime_event (tenant_id, conversation_id, event_name, payload)
                VALUES (1, 1, 'test', '{{}}'), (2, 2, 'test', '{{}}');
            CREATE ROLE queue_runtime NOLOGIN NOSUPERUSER NOBYPASSRLS;
            GRANT USAGE ON SCHEMA public, app TO queue_runtime;
            GRANT SELECT ON whatsapp_realtime_event TO queue_runtime;
            """);
        await using var connection = new Npgsql.NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand("SET ROLE queue_runtime; SET app.current_tenant_id = '1'; SELECT count(*) FROM whatsapp_realtime_event", connection);
        Assert.Equal(1L, await command.ExecuteScalarAsync());
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static void Stop(Process process)
    {
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        Assert.True(process.WaitForExit(5000), "The test process did not stop.");
    }

    private static async Task WaitUntil(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        while (!await condition()) await Task.Delay(200, timeout.Token);
    }

    private sealed class TestClock : IClock
    {
        public DateTime UtcNow { get; set; } = DateTime.UtcNow;
    }
}
