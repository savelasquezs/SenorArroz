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
