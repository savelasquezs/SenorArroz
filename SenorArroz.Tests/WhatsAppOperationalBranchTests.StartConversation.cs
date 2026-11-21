using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using PdfSharp.Pdf.IO;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Shared.Models;

namespace SenorArroz.Tests;

public sealed partial class WhatsAppOperationalBranchTests
{
    [Fact]
    public async Task Start_conversation_sends_the_current_branch_menu_as_a_two_page_document()
    {
        await using var db = CreateDb();
        await SeedStarterLine(db);
        var branch = await db.Branches.SingleAsync();
        branch.MenuImageUrl1 = "https://storage.example.invalid/carta1.png";
        branch.MenuImageUrl2 = "https://storage.example.invalid/carta2.png";
        await db.SaveChangesAsync();
        var image = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAEklEQVR4nGNkmJLFwMDAxAAGAAuoAQInK8WLAAAAAElFTkSuQmCC");
        var storage = new Mock<IFirebaseGcsStorage>();
        storage.Setup(x => x.DownloadObjectFromUrlAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(image);
        storage.Setup(x => x.UploadPublicObjectAsync(It.IsAny<byte[]>(), It.IsAny<string>(), "application/pdf", It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://storage.example.invalid/carta.pdf");
        var cloud = StarterCloud(new("menu", "carta_solicitada", "es_CO", "MARKETING", "APPROVED",
            """[{"type":"HEADER","format":"DOCUMENT"},{"type":"BODY","text":"Carta de {{1}}. Responde para pedir."}]"""));
        cloud.Setup(x => x.UploadMediaAsync("santander-line", "token", It.IsAny<byte[]>(), "Carta Senor Arroz.pdf", "application/pdf", It.IsAny<CancellationToken>()))
            .Callback<string, string, byte[], string, string, CancellationToken>((_, _, pdf, _, _, _) =>
            {
                using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
                Assert.Equal(2, document.PageCount);
            })
            .ReturnsAsync(new WhatsAppCloudUploadMediaResult(true, "uploaded-pdf", null));
        var response = await CreateController(db, cloud: cloud.Object, storage: storage.Object)
            .StartConversation(new(1, "(300) 123-4567", "menu", true, null, null, "santander-line"), default);
        var data = Assert.IsType<ApiResponse<WhatsAppConversationStartResult>>(Assert.IsType<OkObjectResult>(response.Result).Value).Data!;
        Assert.True(data.Sent);
        var conversation = await db.WhatsAppConversations.SingleAsync();
        Assert.Equal((4, 7, "573001234567"), (conversation.BranchLineId, conversation.AssignedUserId, conversation.PhoneNumber));
        Assert.Null(conversation.LastCustomerMessageAt);
        var message = await db.WhatsAppMessages.SingleAsync();
        Assert.Equal(WhatsAppMessageType.Document, message.Type);
        Assert.Equal("uploaded-pdf", message.MediaId);
        Assert.Equal("Carta de Santander. Responde para pedir.", message.TextBody);
        cloud.Verify(x => x.SendSharedContentAsync("santander-line", "token", "573001234567", "template",
            It.Is<JsonElement>(payload => payload.GetProperty("components")[1].GetProperty("parameters")[0]
                .GetProperty("document").GetProperty("id").GetString() == "uploaded-pdf"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Start_conversation_sends_the_approved_catalog_template_to_the_normalized_number()
    {
        await using var db = CreateDb();
        await SeedStarterLine(db);
        var cloud = StarterCloud(new("catalog", "catalogo_solicitado", "es_CO", "MARKETING", "APPROVED",
            """[{"type":"BODY","text":"Catálogo de {{1}}."},{"type":"BUTTONS","buttons":[{"type":"CATALOG"}]}]"""));
        var result = await CreateController(db, cloud: cloud.Object)
            .StartConversation(new(1, "300 123 4567", "catalog", true, null, null, "santander-line"), default);

        var response = Assert.IsType<ApiResponse<WhatsAppConversationStartResult>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.True(response.Data!.Sent);
        Assert.Equal("Catálogo de Santander.", (await db.WhatsAppMessages.SingleAsync()).TextBody);
        cloud.Verify(x => x.SendSharedContentAsync("santander-line", "token", "573001234567", "template",
            It.Is<JsonElement>(payload => payload.GetProperty("name").GetString() == "catalogo_solicitado"
                && payload.GetProperty("components")[1].GetProperty("sub_type").GetString() == "catalog"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Start_conversation_blocks_pending_templates_and_preserves_an_existing_cashiers_chat()
    {
        await using var db = CreateDb();
        await SeedStarterLine(db);
        var template = new WhatsAppCloudTemplate("catalog", "catalogo_solicitado", "es_CO", "MARKETING", "PENDING",
            """[{"type":"BODY","text":"Catálogo de {{1}}. Responde para pedir."},{"type":"BUTTONS","buttons":[{"type":"CATALOG"}]}]""");
        var cloud = StarterCloud(template);
        var controller = CreateController(db, cloud: cloud.Object);
        var request = new WhatsAppConversationStartRequest(1, "3001234567", "catalog", true, null, null, "santander-line");
        Assert.IsType<BadRequestObjectResult>((await controller.StartConversation(request, default)).Result);
        Assert.Empty(await db.WhatsAppConversations.ToListAsync());

        db.WhatsAppConversations.Add(new WhatsAppConversation
        {
            Id = 21, TenantId = 1, BranchId = 1, BranchLineId = 4, AssignedUserId = 8,
            AttentionMode = WhatsAppAttentionMode.Human, PhoneNumber = "573001234567"
        });
        await db.SaveChangesAsync();
        var existing = await controller.StartConversation(request, default);
        var result = Assert.IsType<ApiResponse<WhatsAppConversationStartResult>>(Assert.IsType<OkObjectResult>(existing.Result).Value).Data!;
        Assert.Equal(new WhatsAppConversationStartResult(21, false), result);
        Assert.Equal(8, (await db.WhatsAppConversations.SingleAsync()).AssignedUserId);
        cloud.Verify(x => x.SendSharedContentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Start_conversation_uses_the_branch_bank_account_and_validates_the_customer_order_and_amount()
    {
        await using var db = CreateDb();
        await SeedStarterLine(db);
        db.Customers.Add(new Customer { Id = 12, TenantId = 1, BranchId = 1, Name = "Cliente", Phone1 = "3001234567", Active = true });
        db.Orders.Add(new Order { Id = 25, TenantId = 1, BranchId = 1, CustomerId = 12, TakenById = 7, Total = 45000,
            Status = OrderStatus.Taken, Type = OrderType.Delivery });
        await db.SaveChangesAsync();
        var cloud = StarterCloud(new("bank", "pago_pedido_bancolombia", "es_CO", "UTILITY", "APPROVED",
            """[{"type":"BODY","text":"Señor Arroz {{1}}: pedido {{2}} por {{3}}, cuenta {{4}} a nombre de {{5}}. Gracias."}]"""));
        var options = new WhatsAppCloudOptions
        {
            BancolombiaByBranch = new() { [1] = new() { Account = "de ahorros 00000000000", Holder = "Titular de prueba" } }
        };
        var controller = CreateController(db, cloud: cloud.Object, cloudOptions: options);
        var request = new WhatsAppConversationStartRequest(1, "+57 3001234567", "payment", true, 25, 46000, "santander-line");
        Assert.IsType<BadRequestObjectResult>((await controller.StartConversation(request, default)).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.StartConversation(request with { Phone = "3001234568", Amount = 42000 }, default)).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.StartConversation(request with { RequestedByCustomer = false, Amount = 42000 }, default)).Result);
        Assert.IsType<OkObjectResult>((await controller.StartConversation(request with { Amount = 42000 }, default)).Result);
        var message = await db.WhatsAppMessages.SingleAsync();
        Assert.Contains("pedido #25 por $42.000 COP, cuenta de ahorros 00000000000", message.TextBody);
        cloud.Verify(x => x.SendSharedContentAsync("santander-line", "token", "573001234567", "template",
            It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static async Task SeedStarterLine(SenorArroz.Infrastructure.Data.ApplicationDbContext db)
    {
        db.Branches.Add(new Branch { Id = 1, TenantId = 1, Name = "Santander" });
        db.WhatsAppBranchSettings.Add(new WhatsAppBranchSetting
        {
            Id = 4, TenantId = 1, BranchId = 1, AccessToken = "token", PhoneNumberId = "santander-line",
            BusinessAccountId = "santander-waba", CoexistenceEnabled = true, IsActive = true, IsVerified = true
        });
        await db.SaveChangesAsync();
    }

    private static Mock<IWhatsAppCloudClient> StarterCloud(WhatsAppCloudTemplate template)
    {
        var cloud = new Mock<IWhatsAppCloudClient>();
        cloud.Setup(x => x.GetMessageTemplatesAsync("santander-waba", "token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WhatsAppCloudTemplateSyncResult(true, [template], null));
        cloud.Setup(x => x.SendSharedContentAsync("santander-line", "token", It.IsAny<string>(), "template",
            It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WhatsAppCloudSendResult(true, "wamid.starter", null));
        return cloud;
    }
}
