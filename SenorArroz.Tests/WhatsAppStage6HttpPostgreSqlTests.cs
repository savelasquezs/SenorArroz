using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using AutoMapper;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SenorArroz.API.Controllers;
using SenorArroz.API.Services;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;
using SenorArroz.Application.Features.Orders.Commands;
using SenorArroz.Application.Features.Orders.Queries;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Services;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Repositories;
using SenorArroz.Infrastructure.Services;

namespace SenorArroz.Tests;

public sealed partial class WhatsAppProcessingPostgreSqlTests
{
    private sealed class Stage6Authentication(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var actor = Request.Headers["X-Test-Actor"].ToString();
            if (actor is not ("7" or "8" or "9" or "17" or "18")) return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new[] { new Claim(ClaimTypes.NameIdentifier, actor), new Claim(ClaimTypes.Role, actor == "18" ? "Kitchen" : "Cashier"),
                new Claim("tenant_id", actor == "17" ? "2" : "1"), new Claim("branch_id", actor == "9" ? "2" : actor == "17" ? "3" : "1") };
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
        }
    }

    private async Task<WebApplication> Stage6HttpAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IMapper>(new MapperConfiguration(config => config.AddMaps(typeof(CreateOrderCommand).Assembly), NullLoggerFactory.Instance).CreateMapper());
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddAuthentication("stage6").AddScheme<AuthenticationSchemeOptions, Stage6Authentication>("stage6", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddControllers().AddApplicationPart(typeof(WhatsAppController).Assembly).AddControllersAsServices();
        builder.Services.AddScoped(_ => Db());
        builder.Services.AddScoped<ICurrentUser, CurrentUserService>();
        builder.Services.AddScoped<IBranchContext>(services => new BranchContextService(services.GetRequiredService<IHttpContextAccessor>(),
            services.GetRequiredService<ICurrentUser>(), services.GetRequiredService<ApplicationDbContext>(), _tenant));
        builder.Services.AddScoped(services => {
            var actor = services.GetRequiredService<ICurrentUser>();
            return Stage23Controller(services.GetRequiredService<ApplicationDbContext>(), actor.Id, actor.BranchId, actor.Role,
                services.GetRequiredService<IBranchContext>());
        });
        builder.Services.AddScoped<ISender>(services => WhatsAppOperationalBranchTests.CommercialSender(
            services.GetRequiredService<ApplicationDbContext>(), _clock, services.GetRequiredService<ICurrentUser>().Id).Object);
        var app = builder.Build();
        app.UseAuthentication();
        app.Use(async (context, next) =>
        {
            using var scope = _tenant.BeginTenantScope(int.TryParse(context.User.FindFirstValue("tenant_id"), out var id) ? id : 1);
            try { await next(); }
            catch (SenorArroz.Domain.Exceptions.BranchAccessDeniedException) { context.Response.StatusCode = 403; }
        });
        app.UseAuthorization();
        app.MapControllers();
        app.MapGet("/stage6/pos", async (ApplicationDbContext db, ICurrentUser actor, IMapper mapper, CancellationToken ct) =>
        {
            return await new GetOrdersHandler(new OrderRepository(db, _clock), mapper, actor, db)
                .Handle(new GetOrdersQuery { Page = 1, PageSize = 20 }, ct);
        }).RequireAuthorization();
        await app.StartAsync();
        return app;
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Idle_attention_closes_only_completed_chats_and_releases_a_returning_customer_to_two_cashiers()
    {
        _clock.UtcNow = new DateTime(2026, 11, 19, 15, 0, 0, DateTimeKind.Utc);
        var originalAssignment = _clock.UtcNow.AddHours(-5);
        using (var tenant = _tenant.BeginTenantScope(1))
        {
            await using var db = Db();
            await SeedManualAsync(db);
            db.Users.Add(new User { Id = 8, BranchId = 1, Role = UserRole.Cashier, Name = "Segundo asesor", Email = "second@example.invalid" });
            var first = await db.WhatsAppConversations.SingleAsync();
            first.HumanAssignedAt = first.AttentionModeUpdatedAt = originalAssignment;
            first.LastMessageAt = first.LastCustomerMessageAt = originalAssignment;
            for (var id = 12; id <= 16; id++)
                db.WhatsAppConversations.Add(new WhatsAppConversation
                {
                    Id = id, BranchId = 1, OperationalBranchId = 1, ChannelSettingId = 1, PhoneNumber = $"5730000000{id}",
                    AttentionMode = WhatsAppAttentionMode.Human, AssignedUserId = 7, AttentionModeUpdatedAt = originalAssignment,
                    HumanAssignedAt = originalAssignment, LastMessageAt = originalAssignment,
                    IsPaymentPending = id == 12, NeedsHumanResponse = id == 14,
                    LastCustomerMessageAt = id == 14 ? _clock.UtcNow.AddHours(-3) : originalAssignment
                });
            db.WhatsAppCommerceSessions.Add(new WhatsAppCommerceSession
            { ConversationId = 13, ChannelSettingId = 1, FlowTokenHash = "active-draft", IdempotencyKey = "active-draft" });
            var order = await db.Orders.SingleAsync();
            order.Status = OrderStatus.Delivered;
            order.SetStatusTimes(new() { ["delivered"] = _clock.UtcNow.AddHours(-3) });
            for (var id = 12; id <= 16; id++)
            {
                var subsequentOrder = new Order
                {
                    Id = 100 + id, BranchId = 1, OriginBranchId = 1, TakenById = 7, Type = OrderType.Onsite,
                    WhatsAppConversationId = id, CreatedAt = originalAssignment.AddMinutes(5),
                    Status = id == 16 ? OrderStatus.Ready : OrderStatus.Delivered
                };
                subsequentOrder.SetStatusTimes(new() { ["delivered"] = _clock.UtcNow.AddHours(id == 15 ? -1 : -3) });
                db.Orders.Add(subsequentOrder);
            }
            await db.SaveChangesAsync();
            await db.Orders.Where(x => x.Id == order.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.CreatedAt, originalAssignment.AddMinutes(5)));
        }

        var notifier = Mock.Of<IWhatsAppNotificationService>();
        await using var services = new ServiceCollection()
            .AddSingleton<ITenantExecutionContext>(_tenant).AddSingleton<IClock>(_clock)
            .AddSingleton(notifier).AddScoped<WhatsAppAttentionService>()
            .AddScoped<IApplicationDbContext>(_ => Db()).BuildServiceProvider();
        using var worker = new WhatsAppIdleAttentionWorker(services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WhatsAppIdleAttentionWorker>.Instance);
        Assert.Equal(1, await worker.ScanAsync(default));
        Assert.Equal(0, await worker.ScanAsync(default));

        using var business = _tenant.BeginTenantScope(1);
        await using var verify = Db();
        var saved = await verify.WhatsAppConversations.AsNoTracking().ToListAsync();
        Assert.Equal(WhatsAppAttentionMode.Closed, saved.Single(c => c.Id == 11).AttentionMode);
        Assert.All(saved.Where(c => c.Id != 11), c => Assert.Equal(7, c.AssignedUserId));
        Assert.Empty(await verify.WhatsAppMessages.ToListAsync());

        var cloud = new Mock<IWhatsAppCloudClient>(MockBehavior.Strict);
        async Task Receive(int chatId, string id, DateTime sentAt)
        {
            await using var context = Db();
            var phone = saved.Single(c => c.Id == chatId).PhoneNumber;
            var payload = JsonSerializer.Serialize(new { entry = new[] { new { changes = new[] { new
            {
                field = "messages",
                value = new {
                    metadata = new { phone_number_id = "central" },
                    messages = new[] { new { id, from = phone, timestamp = new DateTimeOffset(sentAt).ToUnixTimeSeconds().ToString(),
                        type = "text", text = new { body = "Tengo una duda" } } }
                }
            } } } } });
            var processor = new WhatsAppWebhookProcessor(context, _clock, cloud.Object, notifier, Mock.Of<IFirebaseGcsStorage>(),
                Options.Create(new FirebaseStorageOptions()), Options.Create(new WhatsAppAiOrchestratorOptions()),
                NullLogger<WhatsAppWebhookProcessor>.Instance, new WhatsAppAttentionService(), Mock.Of<IWhatsAppAiWorkQueue>(),
                Mock.Of<IWhatsAppAutomaticMessageSender>(), Mock.Of<IBranchBusinessHoursService>(), null!, null!, _tenant,
                processing: Options.Create(new WhatsAppProcessingOptions { Mode = "External" }));
            await processor.ProcessAsync(new WhatsAppWebhookEvent(), payload, default);
        }

        await Receive(11, "wamid.older", originalAssignment.AddMinutes(-1));
        Assert.Equal(WhatsAppAttentionMode.Closed, await verify.WhatsAppConversations.Where(c => c.Id == 11).Select(c => c.AttentionMode).SingleAsync());
        _clock.UtcNow = _clock.UtcNow.AddHours(2);
        await Receive(11, "wamid.returned", _clock.UtcNow);
        await Receive(11, "wamid.returned", _clock.UtcNow);
        await Receive(15, "wamid.before-sweep", _clock.UtcNow);
        var reopened = await verify.WhatsAppConversations.AsNoTracking().Where(x => x.Id == 11 || x.Id == 15).ToArrayAsync();
        Assert.All(reopened, c =>
        {
            Assert.Equal(WhatsAppAttentionMode.WaitingForHuman, c.AttentionMode);
            Assert.Null(c.AssignedUserId);
            Assert.Equal(1, c.UnreadCount);
        });
        Assert.Equal(2, await verify.WhatsAppWebhookEvents.CountAsync(x => x.EventType == "attention_action"));

        await using var app = await Stage6HttpAsync();
        using var client = app.GetTestClient();
        var revision = reopened.Single(x => x.Id == 11).AttentionRevision;
        async Task<HttpStatusCode> Take(string actor)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/whatsapp/conversations/11/attention-actions")
                { Content = JsonContent.Create(new { action = "take", expectedVersion = revision }) };
            request.Headers.Add("X-Test-Actor", actor);
            using var response = await client.SendAsync(request);
            return response.StatusCode;
        }
        var takes = await Task.WhenAll(Take("7"), Take("8"));
        Assert.Contains(HttpStatusCode.OK, takes);
        Assert.Contains(HttpStatusCode.Conflict, takes);
        cloud.VerifyNoOtherCalls();
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Quick_reply_preview_resolves_catalog_prices_and_never_sends_editable_replies()
    {
        using (var tenant = _tenant.BeginTenantScope(1))
        {
            await using var db = Db();
            await SeedManualAsync(db);
            db.ProductCategories.Add(new ProductCategory { Id = 1, BranchId = 1, Name = "Arroces", StorefrontRole = "rice" });
            db.Products.AddRange(
                new Product { Id = 1, CategoryId = 1, Name = "Arroz Paisa Súper", Price = 66000, Active = true },
                new Product { Id = 2, CategoryId = 1, Name = "Arroz Paisa con chicharrón Súper", Price = 77000, Active = true },
                new Product { Id = 3, CategoryId = 1, Name = "Arroz Ropa Vieja Dúo", Price = 33000, Active = true });
            db.WhatsAppQuickReplies.AddRange(new WhatsAppQuickReply
            { Id = 1, BranchId = 1, Shortcut = "conchi", ReplyMode = WhatsAppQuickReply.ChicharronQuote },
                new WhatsAppQuickReply { Id = 2, BranchId = 1, Shortcut = "saludo", ReplyMode = WhatsAppQuickReply.Draft, MessageTemplate = "Hola {{nombre_cliente}}" });
            db.WhatsAppMessages.Add(new WhatsAppMessage
            {
                ConversationId = 11, Direction = WhatsAppMessageDirection.Inbound, Type = WhatsAppMessageType.Text,
                Status = WhatsAppMessageStatus.Received, Timestamp = _clock.UtcNow, TextBody = "Paisa súper"
            });
            await db.SaveChangesAsync();
        }
        await using var app = await Stage6HttpAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Actor", "7");
        async Task<JsonElement> Preview(int id, object body)
        {
            using var response = await client.PostAsJsonAsync($"/api/whatsapp/conversations/11/quick-replies/{id}/preview", body);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return json.RootElement.GetProperty("data").Clone();
        }
        var automatic = (await Preview(1, new { })).GetProperty("text").GetString();
        Assert.Contains("77.000", automatic);
        Assert.Contains("66.000", automatic);
        Assert.DoesNotContain("Ropa Vieja", automatic);
        var chosen = (await Preview(1, new { family = "ropa vieja", size = "duo" })).GetProperty("text").GetString();
        Assert.Contains("No disponible", chosen);
        Assert.Contains("33.000", chosen);
        Assert.Equal("Hola cliente", (await Preview(2, new { })).GetProperty("text").GetString());
        using var send = await client.PostAsJsonAsync("/api/whatsapp/conversations/11/messages/quick-reply", new { quickReplyId = 1 });
        Assert.Equal(HttpStatusCode.Conflict, send.StatusCode);
        using var tenant1 = _tenant.BeginTenantScope(1);
        await using var check = Db();
        Assert.Equal(0, await check.WhatsAppMessages.CountAsync(x => x.Direction == WhatsAppMessageDirection.Outbound));
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Two_cashiers_taking_an_empty_chat_create_one_draft_and_no_order()
    {
        using (var tenant = _tenant.BeginTenantScope(1))
        {
            await using var db = Db();
            await SeedManualAsync(db);
            db.Users.Add(new User { Id = 8, BranchId = 1, Name = "Segundo asesor", Email = "second@example.invalid", Role = UserRole.Cashier });
            var conversation = await db.WhatsAppConversations.SingleAsync();
            conversation.AttentionMode = WhatsAppAttentionMode.WaitingForHuman;
            conversation.AssignedUserId = null;
            db.Orders.Remove(await db.Orders.SingleAsync());
            await db.SaveChangesAsync();
        }

        await using var app = await Stage6HttpAsync();
        using var client = app.GetTestClient();
        async Task<HttpStatusCode> Take(string actor)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/whatsapp/conversations/11/attention-actions")
                { Content = JsonContent.Create(new { action = "take", expectedVersion = 0 }) };
            request.Headers.Add("X-Test-Actor", actor);
            using var response = await client.SendAsync(request);
            return response.StatusCode;
        }

        var responses = await Task.WhenAll(Take("7"), Take("8"));
        Assert.Contains(HttpStatusCode.OK, responses);
        Assert.Contains(HttpStatusCode.Conflict, responses);

        using var business = _tenant.BeginTenantScope(1);
        await using var verify = Db();
        var session = await verify.WhatsAppCommerceSessions.SingleAsync(x => x.ConversationId == 11);
        Assert.Equal("active", session.Status);
        Assert.Equal(1, session.BranchId);
        Assert.Null(session.OrderId);
        Assert.Equal(responses[0] == HttpStatusCode.OK ? 7 : 8,
            await verify.WhatsAppConversations.Select(x => x.AssignedUserId).SingleAsync());
        Assert.Empty(await verify.Orders.Where(o => o.WhatsAppConversationId == 11).ToListAsync());
        Assert.Empty(await verify.WhatsAppMessages.ToListAsync());
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Stage6_HTTP_rejects_wrong_tenant_branch_role_and_former_owner_and_deduplicates_confirmation()
    {
        using (var tenant = _tenant.BeginTenantScope(1))
        {
            await using var seed = Db(); await SeedManualAsync(seed);
            seed.Users.AddRange(new User { Id = 8, BranchId = 1, Name = "Segundo asesor", Email = "second@example.invalid", Role = UserRole.Cashier },
                new User { Id = 9, BranchId = 2, Name = "Otra sede", Email = "branch@example.invalid", Role = UserRole.Cashier });
            seed.ProductCategories.Add(new ProductCategory { Id = 30, BranchId = 2, Name = "Arroces" });
            seed.Products.Add(new Product { Id = 31, CategoryId = 30, Name = "Arroz", Price = 10000 });
            seed.WhatsAppConversations.Add(new WhatsAppConversation { Id = 12, BranchId = 2, OperationalBranchId = 2, ChannelSettingId = 1,
                AssignedUserId = 9, AttentionMode = WhatsAppAttentionMode.Human });
            await seed.SaveChangesAsync();
        }
        await using var app = await Stage6HttpAsync();
        using var client = app.GetTestClient();
        async Task<HttpResponseMessage> Request(string actor, HttpMethod method, string path, object? payload = null)
        {
            using var request = new HttpRequestMessage(method, "/api/whatsapp/conversations/" + path);
            if (actor != "") request.Headers.Add("X-Test-Actor", actor);
            if (payload is not null) request.Content = JsonContent.Create(payload);
            return await client.SendAsync(request);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await Request("", HttpMethod.Get, "11")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Request("18", HttpMethod.Get, "11")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Request("17", HttpMethod.Get, "11")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Request("7", HttpMethod.Get, "12")).StatusCode);
        var started = await Request("7", HttpMethod.Post, "11/commercial-draft", new { attentionVersion = 0 });
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        var initial = JsonDocument.Parse(await started.Content.ReadAsStringAsync()).RootElement.GetProperty("data");
        var sessionId = initial.GetProperty("sessionId").GetInt32();
        var saved = await Request("7", HttpMethod.Put, "11/commercial-draft", new {
            sessionId, expectedVersion = 1, attentionVersion = 0,
            order = new { branchId = 2, type = 0, guestName = "Recoge remoto", orderDetails = new[] { new { productId = 31, quantity = 2, unitPrice = 10000 } } }
        });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var takeover = new { action = "takeover", expectedVersion = 0 };
        Assert.Equal(HttpStatusCode.Forbidden, (await Request("9", HttpMethod.Post, "11/attention-actions", takeover)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Request("17", HttpMethod.Post, "11/attention-actions", takeover)).StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionRequired, (await Request("8", HttpMethod.Post, "11/attention-actions", new { action = "takeover" })).StatusCode);
        using (var tenant = _tenant.BeginTenantScope(1))
        {
            await using var db = Db();
            db.WhatsAppWebhookEvents.Add(new WhatsAppWebhookEvent {
                ConversationId = 11, EventType = "manual_operation", ProcessingState = "processing", RequestedByUserId = 7, RawPayload = "{}"
            });
            await db.SaveChangesAsync();
            Assert.Equal(HttpStatusCode.Conflict, (await Request("8", HttpMethod.Post, "11/attention-actions", takeover)).StatusCode);
            await db.WhatsAppWebhookEvents.Where(x => x.EventType == "manual_operation")
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ProcessingState, "completed"));
        }
        Assert.Equal(HttpStatusCode.OK, (await Request("8", HttpMethod.Post, "11/attention-actions", takeover)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Request("7", HttpMethod.Post, "11/attention-actions", takeover)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Request("7", HttpMethod.Post, "11/commercial-draft/confirm",
            new { sessionId, expectedVersion = 2, attentionVersion = 0 })).StatusCode);
        var shared = await (await Request("8", HttpMethod.Get, "11/commercial-draft")).Content.ReadFromJsonAsync<JsonElement>();
        var sameDraft = shared.GetProperty("data");
        Assert.Equal(sessionId, sameDraft.GetProperty("sessionId").GetInt32());
        Assert.Equal(initial.GetProperty("attemptId").GetString(), sameDraft.GetProperty("attemptId").GetString());
        Assert.Equal(2, sameDraft.GetProperty("order").GetProperty("orderDetails")[0].GetProperty("quantity").GetInt32());
        var payload = new { sessionId, expectedVersion = 2, attentionVersion = 1 };
        var replies = await Task.WhenAll(Request("8", HttpMethod.Post, "11/commercial-draft/confirm", payload), Request("8", HttpMethod.Post, "11/commercial-draft/confirm", payload));
        Assert.All(replies, reply => Assert.Equal(HttpStatusCode.OK, reply.StatusCode));
        var ids = await Task.WhenAll(replies.Select(async reply => JsonDocument.Parse(await reply.Content.ReadAsStringAsync()).RootElement.GetProperty("data").GetProperty("orderId").GetInt32()));
        Assert.Equal(ids[0], ids[1]);
        var transferred = await Request("8", HttpMethod.Post, "11/attention-actions", new { action = "transfer", expectedVersion = 1, targetUserId = 7 });
        Assert.Equal(HttpStatusCode.OK, transferred.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Request("8", HttpMethod.Put, "11/commercial-draft", new { sessionId, expectedVersion = 3, attentionVersion = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Request("8", HttpMethod.Post, "11/attention-actions", new { action = "takeover", expectedVersion = 2 })).StatusCode);
        using var tenant1 = _tenant.BeginTenantScope(1);
        await using var check = Db();
        Assert.Equal(ids[0], (await check.WhatsAppCommerceSessions.SingleAsync(x => x.ConversationId == 11)).OrderId);
        var order = await check.Orders.SingleAsync(x => x.Id == ids[0]);
        Assert.Equal(1, order.OriginBranchId); Assert.Equal(2, order.BranchId);
        Assert.Equal(2, await check.Orders.CountAsync()); Assert.Empty(await check.WhatsAppCommerceOutboxMessages.ToListAsync());
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Inbox_labels_count_the_full_team_and_reset_only_older_orders_in_scope()
    {
        _clock.UtcNow = new DateTime(2026, 11, 18, 14, 0, 0, DateTimeKind.Utc);
        using (var tenant = _tenant.BeginTenantScope(1))
        {
            await using var db = Db();
            await SeedManualAsync(db);
            db.Users.AddRange(new User { Id = 8, BranchId = 1, Role = UserRole.Cashier, Name = "Asesor dos", Email = "other@example.invalid" },
                new User { Id = 9, BranchId = 2, Role = UserRole.Cashier, Name = "Otra sucursal", Email = "branch@example.invalid" });
            for (var id = 12; id <= 17; id++)
                db.WhatsAppConversations.Add(new WhatsAppConversation
                {
                    Id = id, BranchId = 1, OperationalBranchId = id == 17 ? 2 : 1, ChannelSettingId = 1,
                    PhoneNumber = $"5730000000{id}", AssignedUserId = id == 17 ? 9 : 8,
                    AttentionMode = WhatsAppAttentionMode.Human, LastCustomerMessageAt = id >= 15 ? _clock.UtcNow.AddDays(-1) : _clock.UtcNow,
                    UnreadCount = id == 13 ? 2 : 0, PendingOrderMarkedAt = id == 16 ? _clock.UtcNow.AddDays(-1) : null
                });
            db.Orders.AddRange(
                new Order { Id = 92, BranchId = 2, OriginBranchId = 1, TakenById = 8, WhatsAppConversationId = 14,
                    CreatedAt = _clock.UtcNow, Status = OrderStatus.Taken },
                new Order { Id = 93, BranchId = 2, OriginBranchId = 1, TakenById = 8, WhatsAppConversationId = 15,
                    CreatedAt = _clock.UtcNow.AddDays(-1), Status = OrderStatus.Taken },
                new Order { Id = 94, BranchId = 1, OriginBranchId = 2, TakenById = 9, WhatsAppConversationId = 17,
                    CreatedAt = _clock.UtcNow.AddDays(-1), Status = OrderStatus.Taken });
            db.WhatsAppCommerceSessions.Add(new WhatsAppCommerceSession
            {
                ConversationId = 12, ChannelSettingId = 1, BranchId = 2, IdempotencyKey = "labels-remote-draft",
                FlowTokenHash = "labels-remote-draft", CreatedAt = _clock.UtcNow, ExpiresAt = _clock.UtcNow.AddHours(1)
            });
            await db.SaveChangesAsync();
        }
        await using var app = await Stage6HttpAsync();
        using var client = app.GetTestClient();
        async Task<HttpResponseMessage> Request(int user, HttpMethod method, string url, object? body = null)
        {
            using var request = new HttpRequestMessage(method, $"/api/whatsapp/{url}");
            request.Headers.Add("X-Test-Actor", user.ToString());
            if (body is not null) request.Content = JsonContent.Create(body);
            return await client.SendAsync(request);
        }
        async Task<JsonElement> Get(int user, string url, HttpMethod? method = null, object? body = null)
        {
            using var response = await Request(user, method ?? HttpMethod.Get, url, body);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return data.RootElement.GetProperty("data").Clone();
        }
        async Task<string?[]> Labels(int user, int chat) =>
            (await Get(user, $"conversations/{chat}")).GetProperty("labels").EnumerateArray().Select(x => x.GetString()).ToArray();
        var firstPage = await Get(7, "conversations/page?inbox=team&limit=1");
        Assert.True(firstPage.GetProperty("hasMore").GetBoolean());
        Assert.Equal(3, (await Get(7, "inbox-summary")).GetProperty("team").GetProperty("pendingOrders").GetInt32());
        Assert.Equal(HttpStatusCode.Forbidden, (await Request(7, HttpMethod.Put, "conversations/13/labels/supplier", new { applied = true })).StatusCode);
        await Get(8, "conversations/13/labels/supplier", HttpMethod.Put, new { applied = true });
        var counters = (await Get(7, "inbox-summary")).GetProperty("team");
        Assert.Equal(2, counters.GetProperty("pendingOrders").GetInt32());
        Assert.Equal(1, counters.GetProperty("unreadConversations").GetInt32());
        Assert.Equal(13, Assert.Single((await Get(7, "conversations/page?inbox=team&label=supplier")).GetProperty("items").EnumerateArray()).GetProperty("id").GetInt32());
        Assert.Contains("branch:2", await Labels(7, 12));
        Assert.Contains("taken", await Labels(7, 14));
        Assert.Contains("taken", await Labels(7, 15));
        Assert.Contains("branch:1", await Labels(9, 17));
        Assert.Equal(HttpStatusCode.Forbidden, (await Request(7, HttpMethod.Post, "labels/taken/reset", new { branchId = 2 })).StatusCode);
        await Get(7, "labels/taken/reset", HttpMethod.Post, new { branchId = 1 });
        Assert.DoesNotContain("taken", await Labels(7, 15));
        Assert.Contains("taken", await Labels(7, 14));
        Assert.Contains("taken", await Labels(9, 17));
        using (var tenant = _tenant.BeginTenantScope(1))
        {
            await using var db = Db();
            (await db.Orders.SingleAsync(x => x.Id == 92)).Status = OrderStatus.Cancelled;
            (await db.WhatsAppCommerceSessions.SingleAsync(x => x.ConversationId == 12)).BranchId = 1;
            await db.SaveChangesAsync();
        }
        Assert.DoesNotContain("taken", await Labels(7, 14));
        Assert.DoesNotContain("branch:2", await Labels(7, 12));
        Assert.Equal(3, (await Get(7, "inbox-summary")).GetProperty("team").GetProperty("pendingOrders").GetInt32());
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Stage6_HTTP_POS_and_inbox_profile_during_240_durable_events()
    {
        using (var tenant = _tenant.BeginTenantScope(1))
        {
            await using var seed = Db(); await SeedManualAsync(seed);
            for (var index = 0; index < 100; index++) seed.Orders.Add(new Order { Id = 1000 + index, BranchId = 1, OriginBranchId = 1, TakenById = 7,
                Type = OrderType.Onsite, Status = OrderStatus.Taken, GuestName = "Local benchmark" });
            await seed.SaveChangesAsync();
        }
        await using var app = await Stage6HttpAsync();
        using var client = app.GetTestClient(); client.DefaultRequestHeaders.Add("X-Test-Actor", "7");
        async Task<double[]> Probe(string route, int count)
        {
            var samples = new List<double>();
            for (var index = 0; index < count; index++)
            {
                var clock = Stopwatch.StartNew();
                using var reply = await client.GetAsync(route); reply.EnsureSuccessStatusCode();
                await reply.Content.ReadAsByteArrayAsync(); samples.Add(clock.Elapsed.TotalMilliseconds);
            }
            return samples.OrderBy(x => x).ToArray();
        }
        await Probe("/stage6/pos", 5); await Probe("/api/whatsapp/conversations/page", 5);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(async index => {
            await using var warm = Db(); await Store(warm).EnqueueAsync(index % 2 + 1,
                WhatsAppProcessingTests.Payload($"stage6-warm-{index}", $"57309999{index:D4}"), default);
        }));
        await using (var warm = Db())
            while (await Store(warm).ClaimAsync(default) is { } job) await Store(warm).FinishAsync(job, true, default);
        var baseline = await Probe("/stage6/pos", 60);
        _clock.UtcNow = DateTime.UtcNow;
        using var process = Process.GetCurrentProcess(); var cpu = process.TotalProcessorTime;
        var enqueue = new List<double>(); var age = 0d; var locks = 0; var connections = 0;
        var produce = Parallel.ForEachAsync(Enumerable.Range(0, 240), new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (index, ct) =>
        {
            await using var db = Db(); var clock = Stopwatch.StartNew();
            await Store(db).EnqueueAsync(index % 2 + 1, WhatsAppProcessingTests.Payload($"stage6-{index}", $"57300000{index % 32:D4}"), ct);
            lock (enqueue) enqueue.Add(clock.Elapsed.TotalMilliseconds);
        });
        var posTask = Probe("/stage6/pos", 60);
        var chatTask = Probe("/api/whatsapp/conversations/page", 60);
        await produce;
        using (_tenant.BeginSystemScope())
        {
            await using var measure = Db();
            var summary = await SenorArroz.API.Services.WhatsAppProcessingMaintenance.ReadAsync(measure, DateTime.UtcNow, default);
            age = summary.OldestPendingSeconds;
            connections = await measure.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE datname=current_database()").SingleAsync();
            locks = await measure.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock'").SingleAsync();
        }
        var drain = Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ => {
            await using var db = Db();
            while (await Store(db).ClaimAsync(default) is { } job) { await Task.Delay(10); await Store(db).FinishAsync(job, true, default); }
        }));
        var loaded = await posTask; var chat = await chatTask;
        static double P95(double[] samples) => Math.Round(samples[(int)Math.Ceiling(samples.Length * .95) - 1], 2);
        var posBefore = P95(baseline); var posAfter = P95(loaded);
        var result = new { capturedAt = DateTime.UtcNow, profile = "Warmed loopback ASP.NET TestServer, singleton production mapper, real POS handler and PostgreSQL; 240 events/32 chats/2 tenants, 8 producers, 4 consumers; no Meta/LLM",
            samples = 60, posBaselineP95Ms = posBefore, posUnderLoadP95Ms = posAfter, chatP95Ms = P95(chat), ingressP95Ms = P95(enqueue.OrderBy(x => x).ToArray()),
            drainSeconds = drain.Elapsed.TotalSeconds, connections, lockWaiters = locks, oldestPendingAgeSeconds = age,
            processCpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds, processWorkingSetBytes = process.WorkingSet64,
            posBudgetMs = posBefore + Math.Max(50, posBefore * .25), posBudgetMet = posAfter <= posBefore + Math.Max(50, posBefore * .25) };
        var outputPath = Path.Combine(RepositoryRoot(), "artifacts", "whatsapp-stage6", "http-load-profile.json");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        output.WriteLine(JsonSerializer.Serialize(result));
        Assert.True(result.posBudgetMet, "POS p95 exceeded the local engineering regression budget.");
        Assert.True(result.chatP95Ms <= 1500); Assert.True(result.ingressP95Ms <= 500);
    }
}
