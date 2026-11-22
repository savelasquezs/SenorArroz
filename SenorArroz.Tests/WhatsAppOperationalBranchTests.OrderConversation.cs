using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.Services;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Services;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Shared.Models;

namespace SenorArroz.Tests;

public sealed partial class WhatsAppOperationalBranchTests
{
    [Theory]
    [InlineData("(300) 123-4567", "573001234567")]
    [InlineData("+57 300 123 4567", "3001234567")]
    [InlineData("+1 415 555 2671", "14155552671")]
    [InlineData(null, null)]
    [InlineData("6041234567", null)]
    public async Task Order_conversation_resolves_exact_contact(string? contactPhone, string? chatPhone)
    {
        await using var db = CreateDb();
        await SeedStarterLine(db);
        await SeedOrderContact(db, contactPhone, "@Cliente.Arroz");
        db.WhatsAppConversations.AddRange(
            new WhatsAppConversation { Id = 12, TenantId = 1, BranchId = 1, BranchLineId = 4,
                PhoneNumber = chatPhone, WhatsAppUsername = "@cliente.arroz", WhatsAppUserId = "CO.exact" },
            new WhatsAppConversation { Id = 13, TenantId = 1, BranchId = 1, BranchLineId = 4,
                PhoneNumber = chatPhone is null ? null : $"{chatPhone}8", WhatsAppUsername = "@cliente.arroz2",
                WhatsAppUserId = "CO.similar" });
        await db.SaveChangesAsync();

        var result = await CreateController(db).GetOrderConversation(25, OrderConversationOrders(db), default);

        Assert.Equal(new WhatsAppOrderConversationDto("existing", ConversationId: 12), ConversationLookupData(result));
    }

    [Fact]
    public async Task Order_conversation_prefers_the_link_and_reports_ambiguous_unlinked_matches()
    {
        await using var db = CreateDb();
        await SeedCentralConversationAsync(db, operationalBranchId: 1, assignedUserId: 7);
        db.WhatsAppBranchSettings.Add(new WhatsAppBranchSetting { Id = 4, TenantId = 1, BranchId = 1,
            IsActive = true, IsVerified = true, CoexistenceEnabled = true });
        db.WhatsAppConversations.Add(new WhatsAppConversation { Id = 12, TenantId = 1, BranchId = 1, BranchLineId = 4,
            PhoneNumber = "573000000000" });
        await SeedOrderContact(db, "3000000000");
        var controller = CreateController(db);
        var orders = OrderConversationOrders(db);

        var ambiguous = await controller.GetOrderConversation(25, orders, default);
        Assert.Equal(new WhatsAppOrderConversationDto("ambiguous"), ConversationLookupData(ambiguous));

        var order = await db.Orders.SingleAsync();
        order.WhatsAppConversationId = 11;
        await db.SaveChangesAsync();
        Assert.Equal(new WhatsAppOrderConversationDto("existing", ConversationId: 11),
            ConversationLookupData(await controller.GetOrderConversation(25, orders, default)));

        var linked = await db.WhatsAppConversations.SingleAsync(c => c.Id == 11);
        linked.OperationalBranchId = 2;
        linked.AssignedUserId = 8;
        await db.SaveChangesAsync();
        Assert.Equal(new WhatsAppOrderConversationDto("unavailable"),
            ConversationLookupData(await controller.GetOrderConversation(25, orders, default)));
    }

    [Fact]
    public async Task Order_conversation_offers_prefill_for_accessible_orders_without_sending()
    {
        await using var db = CreateDb();
        await SeedStarterLine(db);
        await SeedOrderContact(db, "300 123 4567");
        var order = await db.Orders.SingleAsync();
        order.BranchId = 2;
        order.OriginBranchId = 1;
        await db.SaveChangesAsync();
        var cloud = new Mock<IWhatsAppCloudClient>(MockBehavior.Strict);
        var controller = CreateController(db, cloud: cloud.Object);

        var result = await controller.GetOrderConversation(25, OrderConversationOrders(db), default);
        Assert.Equal(new WhatsAppOrderConversationDto("start", Phone: "+573001234567", BranchId: 1), ConversationLookupData(result));
        Assert.Empty(await db.WhatsAppConversations.ToListAsync());
        Assert.Empty(await db.WhatsAppMessages.ToListAsync());

        await Assert.ThrowsAsync<BranchAccessDeniedException>(() =>
            controller.GetOrderConversation(25, OrderConversationOrders(db, 3), default));
    }

    private static async Task SeedOrderContact(ApplicationDbContext db, string? phone, string? username = null)
    {
        db.Customers.Add(new Customer { Id = 12, TenantId = 1, BranchId = 1, Name = "Cliente", Phone1 = phone,
            WhatsAppUsername = username, Active = true });
        db.Orders.Add(new Order { Id = 25, TenantId = 1, BranchId = 1, TakenById = 7, CustomerId = 12 });
        await db.SaveChangesAsync();
    }

    private static OrderManagementService OrderConversationOrders(ApplicationDbContext db, int branchId = 1)
    {
        var user = Mock.Of<ICurrentUser>(u => u.Id == 7 && u.BranchId == branchId && u.Role == "Cashier" && u.TenantId == 1);
        var branch = new Mock<IBranchContext>();
        branch.SetupGet(x => x.EffectiveBranchId).Returns(branchId);
        branch.Setup(x => x.EnsureAccess(It.Is<int>(id => id != branchId))).Throws<BranchAccessDeniedException>();
        return new(db, user, branch.Object, Mock.Of<IClock>());
    }

    private static WhatsAppOrderConversationDto ConversationLookupData(ActionResult<ApiResponse<WhatsAppOrderConversationDto>> result) =>
        Assert.IsType<ApiResponse<WhatsAppOrderConversationDto>>(Assert.IsType<OkObjectResult>(result.Result).Value).Data!;
}
