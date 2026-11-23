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
        cloud.Setup(x => x.UploadMediaAsync("santander-line", "token", It.IsAny<byte[]>(), "Carta Señor Arroz - Santander.pdf", "application/pdf", It.IsAny<CancellationToken>()))
            .Callback<string, string, byte[], string, string, CancellationToken>((_, _, pdf, _, _, _) =>
            {
                using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
                Assert.Equal(2, document.PageCount);
            })
            .ReturnsAsync(new WhatsAppCloudUploadMediaResult(true, "uploaded-pdf", null));
        var response = await CreateController(db, cloud: cloud.Object, storage: storage.Object)
            .StartConversation(new(1, "(300) 123-4567", "menu", true, null, "santander-line"), default);
        var data = Assert.IsType<ApiResponse<WhatsAppConversationStartResult>>(Assert.IsType<OkObjectResult>(response.Result).Value).Data!;
        Assert.True(data.Sent);
        var conversation = await db.WhatsAppConversations.SingleAsync();
        Assert.Equal((4, 7, "573001234567"), (conversation.BranchLineId, conversation.AssignedUserId, conversation.PhoneNumber));
        Assert.Null(conversation.LastCustomerMessageAt);
        var message = await db.WhatsAppMessages.SingleAsync();
        Assert.Equal(WhatsAppMessageType.Document, message.Type);
        Assert.Equal("uploaded-pdf", message.MediaId);
        Assert.Equal("Carta Señor Arroz - Santander.pdf", message.MediaFileName);
        Assert.Equal("Carta de Santander. Responde para pedir.", message.TextBody);
        cloud.Verify(x => x.SendSharedContentAsync("santander-line", "token", "573001234567", "template",
            It.Is<JsonElement>(payload => payload.GetProperty("components")[1].GetProperty("parameters")[0]
                .GetProperty("document").GetProperty("id").GetString() == "uploaded-pdf"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("APPROVED", "carta_imagen_solicitada")]
    [InlineData("PENDING", "carta_solicitada")]
    public async Task Start_conversation_prefers_the_approved_image_for_a_single_page_menu(
        string imageStatus, string expectedTemplate)
    {
        await using var db = CreateDb();
        await SeedStarterLine(db);
        const string image = "https://storage.example.invalid/carta.jpeg";
        (await db.Branches.SingleAsync()).MenuImageUrl1 = image;
        await db.SaveChangesAsync();
        var storage = new Mock<IFirebaseGcsStorage>(MockBehavior.Strict);
        var cloud = StarterCloud(new("pdf", "carta_solicitada", "es_CO", "MARKETING", "APPROVED",
            """[{"type":"HEADER","format":"DOCUMENT"},{"type":"BODY","text":"Carta de {{1}}."}]"""),
            new WhatsAppCloudTemplate("image", "carta_imagen_solicitada", "es_CO", "MARKETING", imageStatus,
                """[{"type":"HEADER","format":"IMAGE"},{"type":"BODY","text":"Te compartimos la carta solicitada."}]"""));
        var controller = CreateController(db, cloud: cloud.Object, storage: storage.Object);
        var options = await controller.GetConversationStartOptions(1, default);
        var data = Assert.IsType<ApiResponse<WhatsAppConversationStartOptions>>(Assert.IsType<OkObjectResult>(options.Result).Value).Data!;
        var menu = Assert.Single(data.Templates, option => option.Key == "menu");
        Assert.Equal(expectedTemplate, menu.Name);
        Assert.Null(menu.UnavailableReason);

        if (imageStatus != "APPROVED") return;

        var request = new WhatsAppConversationStartRequest(1, "3001234567", "menu", true, null, "santander-line", "carta_solicitada");
        Assert.IsType<BadRequestObjectResult>((await controller.StartConversation(request, default)).Result);
        var sent = await controller.StartConversation(request with { TemplateName = expectedTemplate }, default);
        Assert.IsType<OkObjectResult>(sent.Result);
        var message = await db.WhatsAppMessages.SingleAsync();
        Assert.Equal(WhatsAppMessageType.Image, message.Type);
        Assert.Equal(image, message.MediaUrl);
        Assert.Equal("Te compartimos la carta solicitada.", message.TextBody);
        cloud.Verify(x => x.SendSharedContentAsync("santander-line", "token", "573001234567", "template",
            It.Is<JsonElement>(payload => payload.GetProperty("name").GetString() == "carta_imagen_solicitada"
                && payload.GetProperty("components").GetArrayLength() == 1
                && payload.GetProperty("components")[0].GetProperty("parameters")[0]
                    .GetProperty("image").GetProperty("link").GetString() == image),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("Catálogo de Señor Arroz.", "Catálogo de Señor Arroz.", 0)]
    [InlineData("Catálogo de {{1}}.", "Catálogo de Santander.", 1)]
    public async Task Start_conversation_sends_the_approved_catalog_template_to_the_normalized_number(
        string body, string expected, int buttonIndex)
    {
        await using var db = CreateDb();
        await SeedStarterLine(db);
        var cloud = StarterCloud(new("catalog", "catalogo_solicitado", "es_CO", "MARKETING", "APPROVED",
            JsonSerializer.Serialize<object[]>([
                new { type = "BODY", text = body },
                new { type = "BUTTONS", buttons = new[] { new { type = "CATALOG" } } }
            ])));
        var result = await CreateController(db, cloud: cloud.Object)
            .StartConversation(new(1, "300 123 4567", "catalog", true, null, "santander-line"), default);

        var response = Assert.IsType<ApiResponse<WhatsAppConversationStartResult>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.True(response.Data!.Sent);
        Assert.Equal(expected, (await db.WhatsAppMessages.SingleAsync()).TextBody);
        cloud.Verify(x => x.SendSharedContentAsync("santander-line", "token", "573001234567", "template",
            It.Is<JsonElement>(payload => payload.GetProperty("name").GetString() == "catalogo_solicitado"
                && payload.GetProperty("components").GetArrayLength() == buttonIndex + 1
                && payload.GetProperty("components")[buttonIndex].GetProperty("sub_type").GetString() == "catalog"),
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
        var request = new WhatsAppConversationStartRequest(1, "3001234567", "catalog", true, null, "santander-line");
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
    public async Task Start_conversation_sends_the_branch_bank_account_with_a_manual_amount_to_a_new_contact()
    {
        await using var db = CreateDb();
        await SeedStarterLine(db);
        var cloud = StarterCloud(new("bank", "pago_pedido_bancolombia", "es_CO", "UTILITY", "APPROVED",
            """[{"type":"BODY","text":"Hola, te dejo nuestros datos de pago:\n\nCuenta Bancolombia número {{1}} a nombre de {{2}}.\n\nEl total a pagar es de {{3}}. Muchas gracias."}]"""));
        var options = new WhatsAppCloudOptions
        {
            BancolombiaByBranch = new() { [1] = new() { Account = " 00012345678 ", Holder = " Titular de prueba " } }
        };
        var controller = CreateController(db, cloud: cloud.Object, cloudOptions: options);
        var request = new WhatsAppConversationStartRequest(1, "+57 3001234567", "payment", true, 42000, "santander-line");

        foreach (var invalidAmount in new int?[] { null, 0, -1 })
            Assert.IsType<BadRequestObjectResult>((await controller.StartConversation(request with { Amount = invalidAmount }, default)).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.StartConversation(request with { RequestedByCustomer = false }, default)).Result);
        Assert.Empty(await db.WhatsAppConversations.ToListAsync());
        cloud.Verify(x => x.SendSharedContentAsync("santander-line", "token", It.IsAny<string>(), "template",
            It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()), Times.Never);

        Assert.IsType<OkObjectResult>((await controller.StartConversation(request, default)).Result);
        Assert.Null((await db.WhatsAppConversations.SingleAsync()).CustomerId);
        Assert.Empty(await db.Orders.ToListAsync());
        var message = await db.WhatsAppMessages.SingleAsync();
        Assert.Contains("Cuenta Bancolombia número 00012345678 a nombre de Titular de prueba.", message.TextBody);
        Assert.Contains("El total a pagar es de $42.000 COP.", message.TextBody);
        cloud.Verify(x => x.SendSharedContentAsync("santander-line", "token", "573001234567", "template",
            It.Is<JsonElement>(payload => payload.GetProperty("components")[0].GetProperty("parameters").GetArrayLength() == 3
                && payload.GetProperty("components")[0].GetProperty("parameters")[0].GetProperty("text").GetString() == "00012345678"
                && payload.GetProperty("components")[0].GetProperty("parameters")[1].GetProperty("text").GetString() == "Titular de prueba"
                && payload.GetProperty("components")[0].GetProperty("parameters")[2].GetProperty("text").GetString() == "$42.000 COP"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Start_conversation_lists_and_sends_a_delivery_update_with_one_manual_value()
    {
        await using var db = CreateDb();
        await SeedStarterLine(db);
        var cloud = StarterCloud(new("delivery", "novedad_entrega_domiciliario", "es_CO", "UTILITY", "APPROVED",
            """[{"type":"BODY","text":"El domiciliario nos informa que {{1}}. Quedamos atentos."}]"""));
        var controller = CreateController(db, cloud: cloud.Object);
        var options = await controller.GetConversationStartOptions(1, default);
        var data = Assert.IsType<ApiResponse<WhatsAppConversationStartOptions>>(Assert.IsType<OkObjectResult>(options.Result).Value).Data!;
        var choice = Assert.Single(data.Templates, item => item.Key == "delivery_update");
        Assert.Null(choice.UnavailableReason);

        var request = new WhatsAppConversationStartRequest(1, "3001234567", choice.Key, true, null, "santander-line", choice.Name);
        Assert.IsType<BadRequestObjectResult>((await controller.StartConversation(request, default)).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.StartConversation(request with { DeliveryUpdate = new string('x', 1024) }, default)).Result);
        Assert.Empty(await db.WhatsAppConversations.ToListAsync());

        var conversation = new WhatsAppConversation { Id = 21, TenantId = 1, BranchId = 1, BranchLineId = 4,
            PhoneNumber = "573001234567", AttentionMode = WhatsAppAttentionMode.WaitingForHuman };
        db.WhatsAppConversations.Add(conversation);
        await db.SaveChangesAsync();
        var result = await controller.StartConversation(request with { DeliveryUpdate = "  ya llegó y está intentando contactarte  " }, default);
        var started = Assert.IsType<ApiResponse<WhatsAppConversationStartResult>>(Assert.IsType<OkObjectResult>(result.Result).Value).Data!;
        Assert.Equal(new WhatsAppConversationStartResult(21, true), started);
        Assert.Equal(7, conversation.AssignedUserId);
        Assert.Equal(WhatsAppAttentionMode.Human, conversation.AttentionMode);
        Assert.Equal("El domiciliario nos informa que ya llegó y está intentando contactarte. Quedamos atentos.",
            (await db.WhatsAppMessages.SingleAsync()).TextBody);
        cloud.Verify(x => x.SendSharedContentAsync("santander-line", "token", "573001234567", "template",
            It.Is<JsonElement>(payload => payload.GetProperty("name").GetString() == "novedad_entrega_domiciliario"
                && payload.GetProperty("components")[0].GetProperty("parameters").GetArrayLength() == 1
                && payload.GetProperty("components")[0].GetProperty("parameters")[0].GetProperty("text").GetString()
                    == "ya llegó y está intentando contactarte"), It.IsAny<CancellationToken>()), Times.Once);
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

    private static Mock<IWhatsAppCloudClient> StarterCloud(WhatsAppCloudTemplate template, params WhatsAppCloudTemplate[] alternatives)
    {
        var cloud = new Mock<IWhatsAppCloudClient>();
        cloud.Setup(x => x.GetMessageTemplatesAsync("santander-waba", "token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WhatsAppCloudTemplateSyncResult(true, [template, .. alternatives], null));
        cloud.Setup(x => x.SendSharedContentAsync("santander-line", "token", It.IsAny<string>(), "template",
            It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WhatsAppCloudSendResult(true, "wamid.starter", null));
        return cloud;
    }
}
