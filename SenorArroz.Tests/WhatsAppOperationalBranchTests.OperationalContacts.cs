using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Microsoft.Extensions.Logging;
using SenorArroz.API.Controllers;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Features.Suppliers.DTOs;
using SenorArroz.Application.Features.Suppliers.Queries;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Shared.Models;

namespace SenorArroz.Tests;

public sealed partial class WhatsAppOperationalBranchTests
{
    internal static async Task VerifyQuickOperationalInboxAsync(ApplicationDbContext db)
    {
        await SeedCentralConversationAsync(db, 1, 7);
        var day = new DateTime(2026, 11, 23, 5, 0, 0, DateTimeKind.Utc);
        var now = day.AddHours(4);
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(() => now);
        db.Users.AddRange(Driver(20, 2), Driver(21, 1), Driver(22, 1), Driver(23, 2), Driver(24, 1));
        db.Suppliers.AddRange(
            new Supplier { Name = "Molino Alba", Phone = "3010000040" },
            new Supplier { Name = "Ambiguo", Phone = "3010000020" });
        db.AddRange(Chat(30, "573010000020"), Chat(31, "3010000021"), Chat(32, "+57 301 000 0022"),
            Chat(33, "573010000023"), Chat(34, "+573010000040"),
            Chat(35, "573010000050", WhatsAppAttentionMode.Closed), Chat(36, "573010000024"));
        db.DeliveryWorkSessions.Add(new DeliveryWorkSession
        {
            Id = 50, BranchId = 2, DeliverymanId = 20, DeviceInstallationId = "compartido", DevicePlatform = "android",
            StartedAt = day.AddHours(-9), AutoCloseAt = day.AddHours(-2), EndedAt = day.AddHours(-2),
            Status = DeliveryWorkSessionStatus.Closed, LastCommunicationAt = day.AddHours(-2),
        });
        db.DeliveryWorkPhones.Add(new DeliveryWorkPhone { BranchId = 2, Phone = "573010000050", DeviceInstallationId = "compartido" });
        db.AddRange(Assigned(60, 20, 2, 1, day, OrderStatus.Delivered), Assigned(61, 21, 1, null, day.AddSeconds(-1)),
            Assigned(62, 22, 1, null, day, OrderStatus.Cancelled), Assigned(63, 23, 2, null, day),
            Assigned(64, 22, 1, null, day.AddDays(1)), Assigned(65, null, 1, null, day), Assigned(66, 24, 1, null, now));
        await db.SaveChangesAsync();
        var controller = CreateController(db, clock: clock.Object);

        var drivers = await Read(new() { DeliverymenAssignedToday = true, Search = "Reparto 20" });
        var assigned = Assert.Single(drivers.Items);
        Assert.Equal(35, assigned.Id);
        Assert.Equal(true, assigned.MatchesContactQuickFilter);
        Assert.Equal("Reparto 20", assigned.OperationalContact?.Name);
        Assert.Empty((await Read(new() { DeliverymenAssignedToday = true, Inbox = "mine" })).Items);
        var supplier = Assert.Single((await Read(new() { SuppliersOnly = true, Search = "Molino Alba" })).Items);
        Assert.Equal(34, supplier.Id);
        Assert.IsType<BadRequestObjectResult>((await controller.GetConversationPage(new()
        { SuppliersOnly = true, DeliverymenAssignedToday = true }, default)).Result);
        var first = await Read(new() { DeliverymenAssignedToday = true, Limit = 1 });
        Assert.True(first.HasMore);
        var second = await Read(new() { DeliverymenAssignedToday = true, Limit = 1, Cursor = first.NextCursor });
        Assert.Equal(new[] { 35, 36 }, first.Items.Concat(second.Items).Select(x => x.Id).Order());
        now = day.AddDays(1);
        Assert.IsType<BadRequestObjectResult>((await controller.GetConversationPage(new()
        { DeliverymenAssignedToday = true, Limit = 1, Cursor = first.NextCursor }, default)).Result);
        now = day.AddHours(4);

        var reassigned = await db.Orders.SingleAsync(x => x.Id == 60);
        reassigned.BranchId = 1;
        reassigned.DeliveryManId = 21;
        reassigned.TouchDeliveryManAssignedAtUtc(now);
        await db.SaveChangesAsync();
        Assert.Equal(new[] { 31, 36 }, (await Read(new() { DeliverymenAssignedToday = true })).Items.Select(x => x.Id).Order());

        async Task<WhatsAppConversationPageDto> Read(WhatsAppConversationSearchDto request) =>
            Assert.IsType<ApiResponse<WhatsAppConversationPageDto>>(
                Assert.IsType<OkObjectResult>((await controller.GetConversationPage(request, default)).Result).Value).Data!;

        static User Driver(int id, int branch) => new()
        { Id = id, BranchId = branch, Role = UserRole.Deliveryman, Name = $"Reparto {id}", Email = $"driver{id}@demo.test", Phone = $"30100000{id}" };
        static WhatsAppConversation Chat(int id, string phone, WhatsAppAttentionMode attention = WhatsAppAttentionMode.WaitingForHuman) => new()
        { Id = id, TenantId = 1, BranchId = 1, ChannelSettingId = 1, PhoneNumber = phone, AttentionMode = attention };
        static Order Assigned(int id, int? driver, int branch, int? origin, DateTime at, OrderStatus status = OrderStatus.Ready)
        {
            var order = new Order { Id = id, BranchId = branch, OriginBranchId = origin, TakenById = 20,
                Type = OrderType.Delivery, Status = status, DeliveryManId = driver };
            order.TouchDeliveryManAssignedAtUtc(at);
            return order;
        }
    }

    [Fact]
    public async Task Quick_operational_inbox_filters_by_today_assignment_and_supplier()
    {
        await using var db = CreateDb();
        await VerifyQuickOperationalInboxAsync(db);
    }

    [Fact]
    public async Task Cashier_can_preview_and_send_a_quoted_quick_reply_from_their_work_branch()
    {
        await using var db = CreateDb();
        await SeedCentralConversationAsync(db, 2, 7);
        var now = DateTime.UtcNow;
        var conversation = await db.WhatsAppConversations.SingleAsync();
        conversation.LastCustomerMessageAt = now;
        db.AddRange(
            new WhatsAppQuickReply { Id = 20, BranchId = 2, Shortcut = "recibido", MessageTemplate = "Listo, recibido." },
            new WhatsAppQuickReply { Id = 21, BranchId = 1, Shortcut = "recibido", MessageTemplate = "Respuesta de otra sucursal." },
            new WhatsAppMessage { Id = 22, ConversationId = 11, WhatsAppMessageId = "wamid.question",
                Direction = WhatsAppMessageDirection.Inbound, Status = WhatsAppMessageStatus.Received, Timestamp = now });
        await db.SaveChangesAsync();
        var cloud = new Mock<IWhatsAppCloudClient>(MockBehavior.Strict);
        cloud.Setup(c => c.SendTextReplyAsync("phone", "token", It.IsAny<string>(), "Listo, recibido.", "wamid.question", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WhatsAppCloudSendResult(true, "wamid.answer", null));
        var controller = CreateController(db, branchId: 2, cloud: cloud.Object);

        Assert.IsType<OkObjectResult>(await controller.PreviewQuickReply(11, 20, new(null, null), default));
        Assert.IsType<NotFoundResult>(await controller.PreviewQuickReply(11, 21, new(null, null), default));
        var invalidQuote = await controller.SendQuickReply(11, new() { QuickReplyId = 20, ReplyToMessageId = 999 }, default);
        Assert.IsType<BadRequestObjectResult>(invalidQuote.Result);
        var sent = await controller.SendQuickReply(11, new() { QuickReplyId = 20, ReplyToMessageId = 22 }, default);
        var message = Assert.IsType<ApiResponse<WhatsAppMessageDto>>(Assert.IsType<OkObjectResult>(sent.Result).Value).Data!;
        Assert.Equal("wamid.question", message.ReplyToWhatsAppMessageId);
        Assert.Equal(1, (await db.WhatsAppQuickReplies.FindAsync(20))!.UsageCount);
        cloud.Verify(c => c.SendTextReplyAsync("phone", "token", It.IsAny<string>(), "Listo, recibido.", "wamid.question", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Operational_contact_keeps_customer_and_manual_sales_after_number_match()
    {
        await using var db = CreateDb();
        await SeedCentralConversationAsync(db, 1, null);
        db.Suppliers.Add(new Supplier { Id = 20, Phone = "+57 (300) 000-0000", Name = "Arroz Redondo" });
        db.Customers.Add(new Customer { Id = 21, BranchId = 1, Phone1 = "3000000000", Name = "Cliente" });
        var conversation = await db.WhatsAppConversations.SingleAsync();
        conversation.CustomerId = 21;
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var chat = Assert.IsType<ApiResponse<WhatsAppConversationDto>>(
            Assert.IsType<OkObjectResult>((await controller.GetConversation(11, default)).Result).Value).Data!;
        Assert.Equal(21, chat.CustomerId);
        Assert.Equal(new WhatsAppOperationalContactDto("supplier", "Arroz Redondo"), chat.OperationalContact);

        var take = await controller.ExecuteAttentionAction(11, new WhatsAppAttentionActionDto
        { Action = "take", ExpectedVersion = 0 }, default);
        Assert.IsType<OkObjectResult>(take.Result);
        Assert.Equal("advisor", (await db.WhatsAppCommerceSessions.SingleAsync()).DraftSource);
        var enableAi = await controller.ExecuteAttentionAction(11, new WhatsAppAttentionActionDto
        { Action = "return-to-ai", ExpectedVersion = conversation.AttentionRevision }, default);
        Assert.IsType<BadRequestObjectResult>(enableAi.Result);
        Assert.Equal(WhatsAppAttentionMode.Human, conversation.AttentionMode);
    }

    [Fact]
    public async Task Supplier_items_use_the_same_frequent_catalog_and_reject_ambiguous_contacts()
    {
        await using var db = CreateDb();
        await SeedCentralConversationAsync(db, 1, null);
        db.AddRange(
            new Supplier { Id = 20, Phone = "300 000-0000", Name = "Arroz Redondo" },
            new ExpenseCategory { Id = 30, Name = "Insumos" },
            new Expense { Id = 31, CategoryId = 30, Name = "Arroz", Unit = ExpenseUnit.Kilo },
            new SupplierExpense { Id = 32, SupplierId = 20, ExpenseId = 31, UsageCount = 5 });
        var chat = await db.WhatsAppConversations.SingleAsync();
        chat.PhoneNumber = "573000000000";
        await db.SaveChangesAsync();
        var sender = new Mock<ISender>();
        var handler = new GetSupplierExpensesHandler(db);
        sender.Setup(x => x.Send(It.IsAny<GetSupplierExpensesQuery>(), It.IsAny<CancellationToken>()))
            .Returns((GetSupplierExpensesQuery query, CancellationToken ct) => handler.Handle(query, ct));

        var result = await CreateController(db).GetConversationSupplierItems(11, sender.Object, default);
        var article = Assert.Single(Assert.IsType<ApiResponse<IReadOnlyList<SupplierExpenseDto>>>(
            Assert.IsType<OkObjectResult>(result).Value).Data!);
        Assert.Equal("Arroz", article.ExpenseName);
        Assert.Equal("Kilo", article.ExpenseUnit);
        Assert.IsType<NotFoundResult>(await CreateController(db, branchId: 2)
            .GetConversationSupplierItems(11, sender.Object, default));

        db.Users.Add(new User { Id = 22, BranchId = 1, Role = UserRole.Deliveryman, Phone = "+573000000000" });
        await db.SaveChangesAsync();
        Assert.IsType<NotFoundResult>(await CreateController(db).GetConversationSupplierItems(11, sender.Object, default));
        chat.PhoneNumber = null;
        chat.WhatsAppUserId = "CO.synthetic";
        Assert.Null(await WhatsAppOperationalContacts.FindAsync(db, chat, default));
    }

    [Fact]
    public async Task Delivery_routes_prioritize_current_and_scope_orders_to_the_workspace()
    {
        await using var db = CreateDb();
        await VerifyOperationalRoutesAsync(db);
    }

    [Fact]
    public async Task Shared_delivery_phones_follow_the_latest_gps_session_per_device()
    {
        await using var db = CreateDb();
        await VerifySharedDeliveryPhonesAsync(db);
    }

    internal static async Task VerifySharedDeliveryPhonesAsync(ApplicationDbContext db)
    {
        await SeedCentralConversationAsync(db, 1, null);
        var now = new DateTime(2026, 11, 22, 18, 0, 0, DateTimeKind.Utc);
        db.AddRange(
            new User { Id = 20, BranchId = 1, Role = UserRole.Deliveryman, Name = "Ana", Phone = "3000000000", Email = "a@demo.test" },
            new User { Id = 21, BranchId = 1, Role = UserRole.Deliveryman, Name = "Luis", Email = "b@demo.test" },
            new User { Id = 22, BranchId = 2, Role = UserRole.Deliveryman, Name = "Lina", Email = "c@demo.test" },
            new User { Id = 23, BranchId = 1, Role = UserRole.Deliveryman, Name = "Iván", Email = "d@demo.test" });
        db.AddRange(
            Session(30, 20, 1, "equipo-a", now.AddHours(-3), closed: true),
            Session(31, 21, 1, "equipo-a", now.AddHours(-1)),
            Session(32, 20, 1, "equipo-b", now.AddHours(-25), closed: true),
            Session(33, 22, 2, "equipo-foraneo", now.AddHours(-2)),
            Session(35, 23, 1, "equipo-c", now.AddHours(-2)));
        await db.SaveChangesAsync();

        var settings = new BranchWhatsAppSettingsController(db,
            Mock.Of<ICurrentUser>(x => x.Role == "Admin" && x.BranchId == 1), Mock.Of<IClock>(x => x.UtcNow == now),
            Mock.Of<IWhatsAppCloudClient>(), Mock.Of<IBranchBusinessHoursService>(),
            new WhatsAppAwayMessageService(), Mock.Of<ILogger<BranchWhatsAppSettingsController>>());
        Assert.IsType<ForbidResult>((await settings.GetDeliveryPhones(2, default)).Result);
        Assert.IsType<NotFoundObjectResult>(await settings.SaveDeliveryPhone(1, new("3020000003", 33), default));
        Assert.IsType<NoContentResult>(await settings.SaveDeliveryPhone(1, new("300 000 0000", 31), default));
        Assert.IsType<ConflictObjectResult>(await settings.SaveDeliveryPhone(1, new("3000000002", 31), default));
        Assert.IsType<NoContentResult>(await settings.SaveDeliveryPhone(1, new("+573000000001", 32), default));
        Assert.IsType<NoContentResult>(await settings.SaveDeliveryPhone(1, new("3020000003", 35), default));
        var local = Assert.IsType<ApiResponse<DeliveryWorkPhoneSettingsDto>>(
            Assert.IsType<OkObjectResult>((await settings.GetDeliveryPhones(1, default)).Result).Value).Data!;
        Assert.Equal([31, 35, 32], local.Devices.Select(d => d.WorkSessionId));
        Assert.Equal(["573000000000", "573000000001", "573020000003"], local.Phones.Select(p => p.Phone));

        var known = await db.WhatsAppConversations.SingleAsync();
        var olderSessionChat = new WhatsAppConversation { TenantId = 1, PhoneNumber = "3000000001" };
        var manual = await WhatsAppOperationalContacts.FindAsync(db, olderSessionChat, default);
        Assert.Equal(WhatsAppOperationalContacts.Deliveryman, manual?.Kind);
        Assert.Equal(20, (await WhatsAppOperationalContacts.FindForRoutesAsync(db, olderSessionChat, now, default))?.Id);
        var dtos = new[]
        {
            new WhatsAppConversationDto { PhoneNumber = known.PhoneNumber },
            new WhatsAppConversationDto { PhoneNumber = olderSessionChat.PhoneNumber },
            new WhatsAppConversationDto { PhoneNumber = "3020000003" },
        };
        await WhatsAppOperationalContacts.EnrichAsync(db, 1, dtos, default, now);
        Assert.Equal(new WhatsAppOperationalContactDto("deliveryman", "Luis", true, 21), dtos[0].OperationalContact);
        Assert.Equal(new WhatsAppOperationalContactDto("deliveryman", "Ana", true, 20), dtos[1].OperationalContact);
        Assert.Equal(new WhatsAppOperationalContactDto("deliveryman", "Iván", true, 23), dtos[2].OperationalContact);

        var first = await db.DeliveryWorkSessions.SingleAsync(x => x.Id == 31);
        first.Close(now, DeliveryWorkSessionEndReason.UserChange);
        await db.SaveChangesAsync();
        Assert.Equal("Luis", (await WhatsAppOperationalContacts.FindForRoutesAsync(db, known, now, default))?.Name);
        db.DeliveryWorkSessions.Add(Session(34, 20, 1, "equipo-a", now.AddMinutes(5)));
        await db.SaveChangesAsync();
        Assert.Equal("Luis", (await WhatsAppOperationalContacts.FindForRoutesAsync(db, known, now, default))?.Name);
        var next = await WhatsAppOperationalContacts.FindForRoutesAsync(db, known, now.AddMinutes(10), default);
        Assert.Equal(20, next?.Id);
        Assert.Equal("Ana", next?.Name);
        Assert.Equal(20, (await WhatsAppOperationalContacts.FindForRoutesAsync(db, known, now.AddDays(1), default))?.Id);
        Assert.Null(await WhatsAppOperationalContacts.FindAsync(db, new() { TenantId = 2, PhoneNumber = known.PhoneNumber }, default));

        static DeliveryWorkSession Session(int id, int driverId, int branchId, string device, DateTime start, bool closed = false) => new()
        {
            Id = id, DeliverymanId = driverId, BranchId = branchId, DeviceInstallationId = device,
            Status = closed ? DeliveryWorkSessionStatus.Closed : DeliveryWorkSessionStatus.Active,
            StartedAt = start, AutoCloseAt = start.AddHours(4), LastCommunicationAt = start,
            EndedAt = closed ? start.AddMinutes(30) : null,
        };
    }

    internal static async Task VerifyOperationalRoutesAsync(ApplicationDbContext db)
    {
        await SeedCentralConversationAsync(db, 1, null);
        var now = DateTime.UtcNow;
        var session = new DeliveryWorkSession
        {
            Id = 24, DeliverymanId = 20, BranchId = 2, StartedAt = now.AddHours(-1),
            AutoCloseAt = now.AddHours(1), LastCommunicationAt = now, DeviceInstallationId = "route-device",
        };
        db.AddRange(new User { Id = 20, BranchId = 2, Role = UserRole.Deliveryman, Phone = "3000000000",
            Name = "Dom", Email = "driver@example.com" }, session);
        (await db.WhatsAppConversations.SingleAsync()).PhoneNumber = "573000000000";
        await db.SaveChangesAsync();

        db.AddRange(Route(31, 4), Route(32, 3), Route(33, 2), Route(34, 0, current: true));
        await db.SaveChangesAsync();
        db.AddRange(OrderFor(51, 31), OrderFor(52, 32), OrderFor(53, 33), OrderFor(54, 34, origin: null),
            OrderFor(55, 34), OrderFor(56, 34));
        await db.SaveChangesAsync();
        db.AddRange(Stop(51, 31, 1), Stop(52, 32, 1), Stop(53, 33, 1), Stop(54, 34, 1),
            Stop(55, 34, 3), Stop(56, 34, 2));
        await db.SaveChangesAsync();
        var controller = CreateController(db);
        var routes = ReadRoutes(await controller.GetConversationDeliveryRoutes(11, default));
        Assert.Equal(new[] { 34, 33 }, routes.Select(r => r.Id));
        Assert.True(routes[0].IsCurrent);
        Assert.Equal(new[] { 56, 55 }, routes[0].Orders.Select(o => o.OrderId));
        Assert.All(routes[0].Orders, o => Assert.Equal(OrderStatus.Ready, o.Status));

        session.Status = DeliveryWorkSessionStatus.Closed;
        session.EndedAt = now;
        await db.SaveChangesAsync();
        routes = ReadRoutes(await controller.GetConversationDeliveryRoutes(11, default));
        Assert.Equal(new[] { 33, 32 }, routes.Select(r => r.Id));
        Assert.All(routes, route => Assert.False(route.IsCurrent));

        var openRoute = await db.DeliveryRoutes.SingleAsync(r => r.Id == 34);
        openRoute.WorkSessionId = null;
        await db.SaveChangesAsync();
        routes = ReadRoutes(await controller.GetConversationDeliveryRoutes(11, default));
        Assert.Equal(new[] { 34, 33 }, routes.Select(r => r.Id));
        Assert.True(routes[0].IsCurrent);

        db.DeliveryWorkPhones.Add(new DeliveryWorkPhone { BranchId = 2, Phone = "573000000000", DeviceInstallationId = "route-device" });
        session.StartedAt = now.AddDays(-2);
        session.EndedAt = now.AddDays(-1);
        await db.SaveChangesAsync();
        Assert.Empty(ReadRoutes(await controller.GetConversationDeliveryRoutes(11, default, expectedDeliverymanId: 21)));
        Assert.Equal(new[] { 34, 33 }, ReadRoutes(await controller.GetConversationDeliveryRoutes(11, default, expectedDeliverymanId: 20)).Select(r => r.Id));

        DeliveryRoute Route(int id, int hoursAgo, bool current = false) => new()
        {
            Id = id, BranchId = 2, DeliverymanId = 20, WorkSessionId = current ? session.Id : null,
            LastAssignmentAtUtc = now.AddHours(-hoursAgo),
            DepartedAtUtc = current ? null : now.AddHours(-hoursAgo).AddMinutes(5),
            CompletedAtUtc = current ? null : now.AddHours(-hoursAgo).AddMinutes(30),
            Status = current ? DeliveryRouteStatus.Open : DeliveryRouteStatus.Completed,
        };
        Order OrderFor(int id, int route, int? origin = 1) => new()
        {
            Id = id, BranchId = 2, OriginBranchId = origin, TakenById = 20, DeliveryManId = 20,
            DeliveryRouteId = route, Type = OrderType.Delivery, Status = route == 34 ? OrderStatus.Ready : OrderStatus.Delivered,
        };
        static DeliveryRouteStop Stop(int order, int route, int sequence) =>
            new() { Id = order, OrderId = order, DeliveryRouteId = route, StopSequence = sequence };
        static IReadOnlyList<WhatsAppRecentDeliveryRouteDto> ReadRoutes(IActionResult result) =>
            Assert.IsType<ApiResponse<IReadOnlyList<WhatsAppRecentDeliveryRouteDto>>>(Assert.IsType<OkObjectResult>(result).Value).Data!;
    }
}
