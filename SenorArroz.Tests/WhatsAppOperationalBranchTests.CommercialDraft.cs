using System.Text.Json;
using AutoMapper;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SenorArroz.API.Services;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Models;
using SenorArroz.Application.Features.Orders.Commands;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Application.Features.Inventory.DTOs;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Services;
using SenorArroz.Infrastructure.Repositories;
using SenorArroz.Shared.Models;

namespace SenorArroz.Tests;

public sealed partial class WhatsAppOperationalBranchTests
{
    private static readonly WhatsAppProductSuggestions.CatalogProduct[] SuggestionCatalog =
    [
        new(1, "Arroz ropa vieja con chicharrón Súper", "Arroz Ropa Vieja con Chicharrón", "rice", null, 7, 9, 66000),
        new(2, "Arroz ropa vieja Súper", "Arroz Ropa Vieja", "rice", null, 7, 9, 61000),
        new(3, "Arroz paisa Dúo", "Arroz Paisa", "rice", null, 2, 3, 31000),
        new(4, "Arroz paisa con chicharrón Dúo", "Arroz Paisa con Chicharrón", "rice", null, 2, 3, 34000),
        new(5, "Papas a la francesa 500 gr", "Papas a la francesa", "addition", "500 g", null, null, 9000),
        new(6, "Papas a la francesa 250 gr", "Papas a la francesa", "addition", "250 g", null, null, 5500),
        new(7, "Yuca x5 unidades", "Yuca", "addition", "5 unidades", null, null, 5000),
        new(8, "Yuca x12 unidades", "Yuca", "addition", "12 unidades", null, null, 11000),
        new(9, "CocaCola 1.5L", "Coca-Cola", "beverage", "1.5 L", null, null, 8000),
        new(10, "CocaCola zero 1.5 l", "Coca-Cola", "beverage", null, null, null, 8000),
        new(11, "CocaCola PET 400", "Coca-Cola", "beverage", "400 ml", null, null, 3500),
        new(12, "Pepsi pet 400", "Pepsi", "beverage", "400 ml", null, null, 3000),
        new(13, "Pepsi Econo", "Pepsi", "beverage", "1 L", null, null, 5000),
        new(14, "Pepsi econo zero", "Pepsi", "beverage", "1 L sin azúcar", null, null, 5000),
        new(15, "Manzana litro y medio", "Manzana", "beverage", "1.5 L", null, null, 6000),
        new(16, "Manzana Personal", "Manzana", "beverage", "Personal", null, null, 3000),
        new(17, "Hit Mango 1Litro", "Hit", "beverage", "1 L", null, null, 6000),
        new(18, "Hit frutos 1Litro", "Hit", "beverage", "1 L", null, null, 6000),
        new(19, "Arroz ropa vieja Personal", "Ropa Vieja", "rice", null, 1, 2, 19000),
        new(20, "Arroz ropa vieja Dúo", "Ropa Vieja", "rice", null, 2, 3, 33000),
        new(21, "Arroz ropa vieja Trío", "Ropa Vieja", "rice", null, 3, 4, 44000),
        new(22, "Arroz ropa vieja con chicharrón Trío", "Ropa Vieja Chich", "rice", null, 3, 4, 50000),
        new(23, "Arroz paisa Familiar", "Paisa", "rice", "Familiar", 5, 6, 53000),
        new(24, "Arroz paisa con chicharrón Familiar", "Paisa Chich", "rice", "Familiar", 5, 6, 61000)
    ];

    [Theory]
    [InlineData("Ropa vieja con chicharrón súper", 1, 1)]
    [InlineData("dos ropavieja super familiar com chicharron", 1, 2)]
    [InlineData("ropa vieja sin chicharrón para 7 personas", 2, 1)]
    [InlineData("2 yucas x5", 7, 2)]
    [InlineData("Yuca x12", 8, 1)]
    [InlineData("Yuca x5 para dos personas", 7, 1)]
    [InlineData("papas de 500 g en 20 minutos", 5, 1)]
    [InlineData("un paiza dúo sin chillaron", 3, 1)]
    [InlineData("2 botellas de Coca-Cola de 1,5 litros", 9, 2)]
    [InlineData("una Coca Cola de litro y medio sin azúcar", 10, 1)]
    [InlineData("coca cola pet 400", 11, 1)]
    [InlineData("4 Pepsi de 400 ml", 12, 4)]
    [InlineData("Pepsi Econo", 13, 1)]
    [InlineData("Pepsi de un litro cero", 14, 1)]
    [InlineData("Manzana litro y medio", 15, 1)]
    [InlineData("manzana personal", 16, 1)]
    [InlineData("2 Hit mango de 1 litro", 17, 2)]
    public void Suggestions_match_catalog_product_and_order_quantity(string input, int productId, int quantity)
    {
        var match = Assert.Single(WhatsAppProductSuggestions.Find(input, SuggestionCatalog));
        Assert.Equal(productId, match.Product!.Id);
        Assert.Equal(quantity, match.Quantity);
        Assert.Null(match.Reply);
    }

    [Fact]
    public void Suggestions_keep_separate_products_and_prepare_price_or_clarification()
    {
        var matches = WhatsAppProductSuggestions.Find("Dos ropa vieja súper con chicharrón y papas de 500 g", SuggestionCatalog);
        Assert.Equal(new[] { (1, 2), (5, 1) }, matches.Select(m => (m.Product!.Id, m.Quantity)));
        var offer = Assert.Single(WhatsAppProductSuggestions.Find("ropa vieja súper", SuggestionCatalog)).Offer!;
        Assert.True(offer.ChicharronVariants);
        Assert.Equal(new[] { 2, 1 }, offer.Products.Select(p => p.Id));
        var price = Assert.Single(WhatsAppProductSuggestions.Find("¿Cuánto vale el paisa dúo sin chicharrón?", SuggestionCatalog));
        Assert.Contains("31.000", price.Reply!);
        Assert.False(price.Clarification);
    }

    [Theory]
    [InlineData("ropa vieja para 1", 19, 0)]
    [InlineData("ropa vieja para dos", 20, 0)]
    [InlineData("ropa vieja para 3", 21, 22)]
    [InlineData("ropa vieja dúo para 3", 20, 0)]
    [InlineData("paisa familiar", 23, 24)]
    public void Suggestions_use_the_requested_size_or_house_portion_defaults(string text, int first, int second)
    {
        var match = Assert.Single(WhatsAppProductSuggestions.Find(text, SuggestionCatalog));
        if (second == 0) Assert.Equal(first, match.Product?.Id);
        else Assert.Equal(new[] { first, second }, match.Offer!.Products.Select(p => p.Id));
        Assert.Equal(1, match.Quantity);
    }

    [Fact]
    public void Suggestions_separate_rice_capacity_from_quantity_and_offer_papas()
    {
        var matches = WhatsAppProductSuggestions.Find("Una caja de 7 porciones ropa vieja y unas Papas", SuggestionCatalog);
        Assert.Equal(2, matches.Count);
        Assert.Equal(new[] { 2, 1 }, matches[0].Offer!.Products.Select(p => p.Id));
        Assert.Equal(new[] { 6, 5 }, matches[1].Offer!.Products.Select(p => p.Id));
        Assert.All(matches, match => Assert.Equal(1, match.Quantity));
    }

    [Fact]
    public void Suggestions_keep_beverage_volume_together_in_a_mixed_order()
    {
        var matches = WhatsAppProductSuggestions.Find(
            "Dos ropa vieja súper con chicharrón y 3 Pepsi de 400 ml, 1 Manzana litro y medio y 2 Coca-Cola Zero de 1,5 litros",
            SuggestionCatalog);
        Assert.Equal(new[] { (1, 2), (12, 3), (15, 1), (10, 2) }, matches.Select(m => (m.Product!.Id, m.Quantity)));
    }

    [Theory]
    [InlineData("No me mandes ropa vieja con chicharrón súper")]
    [InlineData("mejor ropa vieja con chicharron super")]
    [InlineData("ropa vieja con chicharrón súper sin cebolla")]
    [InlineData("ropa vieja súper con y sin chicharrón")]
    [InlineData("1,5 ropa vieja con chicharrón súper")]
    [InlineData("con chicharrón")]
    [InlineData("papas")]
    [InlineData("2 yucas")]
    [InlineData("2 coca cola")]
    [InlineData("Pepsi de 400 ml sin azúcar")]
    [InlineData("Manzana personal de litro y medio")]
    [InlineData("una Coca Cola 1.5 L o 400 ml")]
    [InlineData("2 Hit de un litro")]
    public void Suggestions_leave_unclear_orders_to_the_cashier(string input)
        => Assert.DoesNotContain(WhatsAppProductSuggestions.Find(input, SuggestionCatalog), m => m.Product is not null && m.Reply is null);

    [Fact]
    public async Task Suggested_product_save_checks_owner_price_stock_preparing_branch_and_duplicate()
    {
        await using var db = CreateDb();
        await SeedCentralConversationAsync(db, 1, 7);
        db.ProductCategories.Add(new ProductCategory { Id = 4, BranchId = 1, Name = "Arroces", StorefrontRole = "rice" });
        var product = new Product { Id = 1, CategoryId = 4, Name = "Arroz ropa vieja con chicharrón Súper", Price = 66000 };
        db.Add(product);
        db.WhatsAppMessages.AddRange(
            new WhatsAppMessage { Id = 91, ConversationId = 11, Direction = WhatsAppMessageDirection.Inbound, TextBody = "dos ropa vieja súper con chicharrón" },
            new WhatsAppMessage { Id = 92, ConversationId = 11, Direction = WhatsAppMessageDirection.Outbound, TextBody = "ropa vieja súper con chicharrón" },
            new WhatsAppMessage { Id = 93, ConversationId = 11, Direction = WhatsAppMessageDirection.Inbound, IsHistorical = true, TextBody = "ropa vieja súper con chicharrón" });
        await db.SaveChangesAsync();
        var inventory = new Mock<IInventoryService>();
        inventory.Setup(x => x.GetAvailabilityAsync(It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), true))
            .ReturnsAsync((IReadOnlyCollection<int> ids, int branch, CancellationToken _, bool _) =>
                ids.Select(id => new ProductAvailabilityDto(id, true, branch == 1 ? 5 : 2, InventoryControlMode.Strict, false, [])).ToArray());
        var controller = CreateController(db, inventory: inventory.Object);
        var result = Assert.IsType<ApiResponse<WhatsAppProductSuggestionsDto>>(Assert.IsType<OkObjectResult>(
            await controller.GetProductSuggestions(11, new([91, 92, 93]), default)).Value).Data!;
        Assert.Equal(1, result.BranchId);
        Assert.Equal(91, Assert.Single(result.Messages).MessageId);
        Assert.True(Assert.Single(result.Messages[0].Suggestions).Available);
        Assert.Empty(await db.WhatsAppCommerceSessions.ToListAsync());

        var started = DraftPayload(await controller.StartCommercialDraft(11, new(), default));
        var request = new WhatsAppDraftWriteDto { SessionId = started.SessionId, ExpectedVersion = started.Version,
            Order = new() { BranchId = 2, OrderDetails =
            [
                new() { ProductId = 1, Quantity = 1, UnitPrice = 66000 },
                new() { ProductId = 1, Quantity = 2, UnitPrice = 66000, WhatsAppSuggestionMessageId = 91 },
            ] } };
        Assert.IsType<ConflictObjectResult>(await CreateController(db, userId: 8).SaveCommercialDraft(11, request, default));
        await Assert.ThrowsAsync<BusinessException>(() => controller.SaveCommercialDraft(11, request, default));
        inventory.Verify(x => x.GetAvailabilityAsync(It.IsAny<IReadOnlyCollection<int>>(), 2, It.IsAny<CancellationToken>(), true), Times.Once);
        request.Order.OrderDetails.RemoveAt(0);
        product.Price = 67000;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<BusinessException>(() => controller.SaveCommercialDraft(11, request, default));
        request.Order.OrderDetails[0].UnitPrice = 67000;
        var saved = DraftPayload(await controller.SaveCommercialDraft(11, request, default));
        Assert.Equal(2, saved.Order.BranchId);
        Assert.Equal(91, Assert.Single(saved.Order.OrderDetails).WhatsAppSuggestionMessageId);
        Assert.IsType<ConflictObjectResult>(await controller.SaveCommercialDraft(11, request, default));
        request.ExpectedVersion = saved.Version;
        request.Order.OrderDetails.Add(request.Order.OrderDetails[0]);
        await Assert.ThrowsAsync<BusinessException>(() => controller.SaveCommercialDraft(11, request, default));
        Assert.Single(DraftPayload(await controller.GetCommercialDraft(11, default)).Order.OrderDetails);
        Assert.Empty(await db.Orders.ToListAsync());
    }

    [Fact]
    public async Task Catalog_cart_suggests_only_verified_skus_with_customer_quantities_and_current_prices()
    {
        await using var db = CreateDb();
        await SeedCentralConversationAsync(db, 1, 7);
        db.ProductCategories.Add(new ProductCategory { Id = 4, BranchId = 1, Name = "Arroces", StorefrontRole = "rice" });
        db.Products.Add(new Product { Id = 26, CategoryId = 4, Name = "Arroz paisa Personal", Price = 19000 });
        db.WhatsAppMessages.Add(new WhatsAppMessage
        {
            Id = 91, ConversationId = 11, Direction = WhatsAppMessageDirection.Inbound, Type = WhatsAppMessageType.Text,
            AiProcessingError = WhatsAppCatalogCart.ManualReview, TextBody = "Carrito de WhatsApp",
            RawPayload = """
                {"type":"order","order":{"catalog_id":"catalog-1","text":"Arroz paisa personal sin chicharrón",
                "product_items":[{"product_retailer_id":"personal","quantity":"2","item_price":"18000","currency":"COP"},
                {"product_retailer_id":"unmapped","quantity":"1","item_price":"19000","currency":"COP"}]}}
                """
        });
        await db.SaveChangesAsync();
        var options = new WhatsAppCloudOptions { CatalogProductIds = new() { ["catalog-1"] = new() { ["personal"] = 26 } } };
        var controller = CreateController(db, cloudOptions: options);
        var result = Assert.IsType<ApiResponse<WhatsAppProductSuggestionsDto>>(Assert.IsType<OkObjectResult>(
            await controller.GetProductSuggestions(11, new([91]), default)).Value).Data!;
        var suggestion = Assert.Single(Assert.Single(result.Messages).Suggestions);
        Assert.Equal(("add", 26, 2, 19000), (suggestion.Kind, suggestion.ProductId, suggestion.Quantity, suggestion.UnitPrice));

        var started = DraftPayload(await controller.StartCommercialDraft(11, new(), default));
        var request = new WhatsAppDraftWriteDto { SessionId = started.SessionId, ExpectedVersion = started.Version,
            Order = new() { BranchId = 1, OrderDetails = [
                new() { ProductId = 26, Quantity = 2, UnitPrice = 19000, WhatsAppSuggestionMessageId = 91 }
            ] } };
        var saved = DraftPayload(await controller.SaveCommercialDraft(11, request, default));
        Assert.Equal(91, Assert.Single(saved.Order.OrderDetails).WhatsAppSuggestionMessageId);
        Assert.Empty(await db.Orders.ToListAsync());
    }

    [Fact]
    public async Task Product_suggestions_use_public_menu_prices_and_skip_internal_gifts()
    {
        await using var db = CreateDb();
        await SeedCentralConversationAsync(db, 1, 7);
        db.ProductCategories.AddRange(
            new ProductCategory { Id = 5, BranchId = 1, Name = "Adiciones", StorefrontRole = "addition" },
            new ProductCategory { Id = 6, BranchId = 1, Name = "Regalos", StorefrontRole = "hidden" });
        db.Products.AddRange(
            new Product { Id = 39, CategoryId = 5, Name = "Chicharrón 250 gr", Price = 12000 },
            new Product { Id = 94, CategoryId = 6, Name = "Chicharron 250g", Price = 0 },
            new Product { Id = 93, CategoryId = 6, Name = "Gaseosa litro y medio", Price = 0 });
        db.WhatsAppMessages.AddRange(
            new WhatsAppMessage { Id = 91, ConversationId = 11, Direction = WhatsAppMessageDirection.Inbound, TextBody = "Un chicharrón 250g" },
            new WhatsAppMessage { Id = 92, ConversationId = 11, Direction = WhatsAppMessageDirection.Inbound, TextBody = "Gaseosa litro y medio" });
        await db.SaveChangesAsync();

        var result = Assert.IsType<ApiResponse<WhatsAppProductSuggestionsDto>>(Assert.IsType<OkObjectResult>(
            await CreateController(db).GetProductSuggestions(11, new([91, 92]), default)).Value).Data!;
        var product = Assert.Single(result.Messages.Single(row => row.MessageId == 91).Suggestions);
        Assert.Equal((39, 12000, 1), (product.ProductId, product.UnitPrice, product.Quantity));
        Assert.Empty(result.Messages.Single(row => row.MessageId == 92).Suggestions);
    }

    [Theory]
    [InlineData("Arroz Paisa con Chicharrón", true, "paisacon")]
    [InlineData("Arroz Paisa", true, "paisasin")]
    [InlineData("Arroz Ropa Vieja con Chicharrón", true, "ropaviejacon")]
    [InlineData("Arroz Ropa Vieja", true, "ropaviejasin")]
    [InlineData("Arroz Carbonara", true, "carbonara")]
    [InlineData("Arroz Ranchero", true, "ranchero")]
    [InlineData("Arroz Vegetariano", true, "vegetariano")]
    [InlineData("Papas a la francesa", false, "papas")]
    [InlineData("Yuca", false, "yucas")]
    [InlineData("Pan", false, null)]
    public void Catalog_shortcuts_use_the_commercial_family(string name, bool isRice, string? shortcut) =>
        Assert.Equal(shortcut, WhatsAppCommercialDraft.ProductChoiceShortcut(name, isRice));

    [Theory]
    [InlineData("Arroz Paisa", 5, false, false)]
    [InlineData("Papas a la francesa", 2, false, false)]
    [InlineData("Yuca", 2, false, false)]
    [InlineData("Arroz Paisa", 5, true, false)]
    [InlineData("Ropa Vieja", 2, false, true)]
    [InlineData("Ropa Vieja", 2, true, true)]
    public async Task Product_choices_use_catalog_prices_and_apply_once_to_the_active_draft(string name, int count, bool priceChanged, bool contextual)
    {
        const int groupId = 3;
        const string recipient = "573121234567";
        await using var db = CreateDb();
        await SeedCentralConversationAsync(db, 2, 7);
        var conversation = await db.WhatsAppConversations.SingleAsync();
        conversation.PhoneNumber = recipient;
        conversation.LastCustomerMessageAt = DateTime.UtcNow;
        db.CommercialProfiles.Add(new CommercialProfile { Id = groupId, BranchId = 1, Name = name });
        var isRice = count == 5;
        const string selectedVariant = "Arroz ropa vieja con chicharrón Súper";
        db.ProductCategories.Add(new ProductCategory { Id = 4, BranchId = 1, Name = name,
            StorefrontRole = isRice || contextual ? "rice" : "addition" });
        if (contextual)
        {
            db.CommercialProfiles.Add(new CommercialProfile { Id = groupId + 1, BranchId = 1, Name = "Ropa Vieja Chich" });
            db.Products.AddRange(
                new Product { Id = 1, CategoryId = 4, CommercialProfileId = groupId, Name = "Arroz ropa vieja Súper",
                    Price = 77000, ServesPeopleMin = 7, ServesPeopleMax = 9 },
                new Product { Id = 2, CategoryId = 4, CommercialProfileId = groupId + 1, Name = selectedVariant,
                    Price = 84000, ServesPeopleMin = 7, ServesPeopleMax = 9 });
            db.WhatsAppMessages.Add(new WhatsAppMessage { Id = 91, ConversationId = 11, Direction = WhatsAppMessageDirection.Inbound,
                TextBody = "Dos cajas de 7 porciones ropa vieja" });
        }
        else
        {
            for (var i = 1; i <= count; i++)
                db.Products.Add(new Product { Id = i, CategoryId = 4, CommercialProfileId = groupId,
                    Name = $"{name} {i}", StorefrontVariantLabel = $"Tamaño {i}", Price = 5000 * i,
                    ServesPeopleMin = isRice ? i : null, ServesPeopleMax = isRice ? i + 1 : null });
        }
        await db.SaveChangesAsync();

        string? optionId = null;
        var cloud = new Mock<IWhatsAppCloudClient>();
        cloud.Setup(x => x.SendListMessageAsync("phone", "token", recipient, It.Is<string>(body => body.Contains(name)), "Ver opciones",
            It.IsAny<IReadOnlyList<WhatsAppListOption>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string _, string _, string _, string _, IReadOnlyList<WhatsAppListOption> rows, CancellationToken _) =>
            {
                Assert.True(isRice);
                Assert.Equal(5, rows.Count);
                Assert.Equal("Tamaño 2", rows[1].Title);
                Assert.Equal("$10.000 · 2–3 personas", rows[1].Description);
                optionId = rows[1].Id;
                return new(true, "product-prompt", null);
            });
        cloud.Setup(x => x.SendReplyButtonsMessageAsync("phone", "token", recipient, It.Is<string>(body => body.Contains(name)),
            It.IsAny<IReadOnlyList<WhatsAppReplyButton>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string _, string _, string body, IReadOnlyList<WhatsAppReplyButton> buttons, CancellationToken _) =>
            {
                Assert.False(isRice);
                Assert.Equal(2, buttons.Count);
                Assert.Equal(contextual ? "Con chicharrón" : "Tamaño 2 · $10.000", buttons[1].Title);
                if (contextual)
                {
                    Assert.Equal("Sin chicharrón", buttons[0].Title);
                    Assert.Contains("Ropa Vieja · Súper", body);
                    Assert.Contains("Sin chicharrón · $77.000", body);
                    Assert.Contains("Con chicharrón · $84.000", body);
                }
                optionId = buttons[1].Id;
                return new(true, "product-prompt", null);
            });
        var controller = CreateController(db, cloud: cloud.Object);
        var groups = Assert.IsType<ApiResponse<WhatsAppProductChoiceGroupDto[]>>(
            Assert.IsType<OkObjectResult>(await controller.GetCommercialProductGroups(default)).Value).Data;
        Assert.Contains(groups!, group => group.Name == name && group.Id == groupId);
        var draft = DraftPayload(await controller.StartCommercialDraft(11, new(), default));
        var saved = DraftPayload(await controller.SaveCommercialDraft(11, new()
        {
            SessionId = draft.SessionId, ExpectedVersion = draft.Version,
            Order = new() { BranchId = 2, Type = OrderType.Delivery, Notes = "Mantener nota del cajero",
                OrderDetails = [new() { ProductId = 1, Quantity = 3, UnitPrice = contextual ? 77000 : 5000 }] },
            Editor = JsonSerializer.SerializeToElement(new { orderItems = new[] { new { productId = 1, quantity = 3 } } })
        }, default));
        var request = new WhatsAppDraftWriteDto { SessionId = saved.SessionId, ExpectedVersion = saved.Version };
        if (contextual)
        {
            var result = Assert.IsType<OkObjectResult>(await controller.GetProductSuggestions(11, new([91]), default));
            var suggestions = Assert.IsType<ApiResponse<WhatsAppProductSuggestionsDto>>(result.Value).Data!;
            var offer = Assert.Single(Assert.Single(suggestions.Messages).Suggestions);
            Assert.Equal("choices", offer.Kind);
            Assert.Equal(2, offer.Quantity);
            Assert.Equal(groupId, offer.ChoiceGroupId);
            request.ProductChoiceSource = new(91, offer.ChoiceIndex!.Value, offer.ProductId!.Value);
        }
        var sent = await controller.SendCommercialProductChoices(11, groupId, request, default);
        Assert.IsType<OkObjectResult>(sent.Result);
        Assert.NotNull(optionId);
        if (priceChanged)
        {
            (await db.Products.SingleAsync(x => x.Id == 2)).Price = 11000;
            await db.SaveChangesAsync();
        }

        var processor = new WhatsAppWebhookProcessor(db, Mock.Of<IClock>(x => x.UtcNow == DateTime.UtcNow),
            cloud.Object, Mock.Of<IWhatsAppNotificationService>(), Mock.Of<IFirebaseGcsStorage>(), Options.Create(new FirebaseStorageOptions()),
            Options.Create(new WhatsAppAiOrchestratorOptions()), NullLogger<WhatsAppWebhookProcessor>.Instance,
            new WhatsAppAttentionService(), Mock.Of<IWhatsAppAiWorkQueue>(), Mock.Of<IWhatsAppAutomaticMessageSender>(),
            Mock.Of<IBranchBusinessHoursService>(), null!, null!, TestTenantContext.Default);
        var replyType = isRice ? "list_reply" : "button_reply";
        string Payload(string id) => JsonSerializer.Serialize(new
        {
            entry = new[] { new { changes = new[] { new { field = "messages", value = new
            {
                metadata = new { phone_number_id = "phone" },
                messages = new[] { new {
                    from = recipient, id, type = "interactive", context = new { id = "product-prompt" },
                    interactive = new Dictionary<string, object> { ["type"] = replyType,
                        [replyType] = new { id = optionId, title = "Tamaño 2" } }
                } }
            } } } } }
        });
        await processor.ProcessAsync(new WhatsAppWebhookEvent(), Payload("product-answer-1"), default);
        await processor.ProcessAsync(new WhatsAppWebhookEvent(), Payload("product-answer-1"), default);
        await processor.ProcessAsync(new WhatsAppWebhookEvent(), Payload("product-answer-2"), default);
        var loaded = DraftPayload(await controller.GetCommercialDraft(11, default));
        Assert.Equal(saved.Version + (priceChanged ? 0 : 1), loaded.Version);
        Assert.Equal(2, loaded.Order.BranchId);
        Assert.Equal("Mantener nota del cajero", loaded.Order.Notes);
        Assert.Equal(3, loaded.Order.OrderDetails[0].Quantity);
        Assert.Equal(priceChanged ? 1 : 2, loaded.Order.OrderDetails.Count);
        if (!priceChanged)
        {
            var chosen = loaded.Order.OrderDetails[1];
            Assert.Equal((2, contextual ? 2 : 1, contextual ? 84000 : 10000), (chosen.ProductId, chosen.Quantity, chosen.UnitPrice));
            Assert.Equal(contextual ? selectedVariant : $"{name} 2", chosen.WhatsAppProductChoice?.Name);
            Assert.Equal(2, loaded.Editor!.Value.GetProperty("orderItems")[1].GetProperty("productId").GetInt32());
        }
        Assert.Empty(db.Orders);
    }

    [Theory]
    [InlineData(2, null)]
    [InlineData(7, null)]
    [InlineData(7, "coverage")]
    [InlineData(7, "old_context")]
    [InlineData(7, "newer_prompt")]
    [InlineData(7, "manual_address")]
    public async Task Address_choice_uses_current_order_coverage_and_only_the_linked_prompt(int count, string? stale)
    {
        const string recipient = "573121234567";
        await using var db = CreateDb();
        await SeedCentralConversationAsync(db, 2, 7);
        var conversation = await db.WhatsAppConversations.SingleAsync();
        conversation.PhoneNumber = recipient;
        conversation.CustomerId = 20;
        conversation.LastCustomerMessageAt = DateTime.UtcNow;
        db.Customers.Add(new Customer { Id = 20, Name = "Cliente de pruebas", Phone1 = "3121234567", Active = true });
        for (var i = 1; i <= count + 1; i++)
        {
            db.Addresses.Add(new Address { Id = i, CustomerId = 20, AddressText = $"Calle {i} # 1-2", AdditionalInfo = "A3" });
            db.AddressBranches.Add(new AddressBranch { AddressId = i, BranchId = i <= count ? 2 : 1, DeliveryFee = 3500, IsCovered = true });
        }
        await db.SaveChangesAsync();
        string? optionId = null;
        var sendCount = 0;
        var cloud = new Mock<IWhatsAppCloudClient>();
        cloud.Setup(x => x.SendListMessageAsync("phone", "token", recipient, It.IsAny<string>(), "Ver direcciones",
                It.IsAny<IReadOnlyList<WhatsAppListOption>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string _, string _, string _, string _, IReadOnlyList<WhatsAppListOption> rows, CancellationToken _) =>
            {
                Assert.True(count > 3);
                Assert.Equal(count, rows.Count);
                optionId = rows[0].Id;
                return new(true, $"prompt-{++sendCount}", null);
            });
        cloud.Setup(x => x.SendReplyButtonsMessageAsync("phone", "token", recipient, It.IsAny<string>(),
                It.IsAny<IReadOnlyList<WhatsAppReplyButton>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string _, string _, string _, IReadOnlyList<WhatsAppReplyButton> buttons, CancellationToken _) =>
            {
                Assert.True(count <= 3);
                Assert.Equal(count, buttons.Count);
                optionId = buttons[0].Id;
                return new(true, $"prompt-{++sendCount}", null);
            });
        var controller = CreateController(db, cloud: cloud.Object);
        var draft = DraftPayload(await controller.StartCommercialDraft(11, new(), default));
        var saved = DraftPayload(await controller.SaveCommercialDraft(11, new()
        {
            SessionId = draft.SessionId, ExpectedVersion = draft.Version,
            Order = new() { BranchId = 2, CustomerId = 20, Type = OrderType.Delivery, Notes = "Conservar este pedido" },
            Editor = JsonSerializer.SerializeToElement(new { addressId = (int?)null, orderItems = new[] { 41 } })
        }, default));
        var request = new WhatsAppDraftWriteDto { SessionId = saved.SessionId, ExpectedVersion = saved.Version };
        var sent = await controller.SendCommercialAddressChoices(11, request, default);
        Assert.IsType<OkObjectResult>(sent.Result);
        Assert.Equal(1, sendCount);
        Assert.NotNull(optionId);
        var clickedOption = optionId;
        var session = await db.WhatsAppCommerceSessions.SingleAsync();
        if (stale == "coverage") (await db.AddressBranches.SingleAsync(x => x.AddressId == 1)).IsCovered = false;
        if (stale == "newer_prompt") Assert.IsType<OkObjectResult>((await controller.SendCommercialAddressChoices(11, request, default)).Result);
        if (stale == "manual_address")
        {
            var changed = WhatsAppCommercialDraft.Read(session);
            changed.AddressId = 2;
            WhatsAppCommercialDraft.Write(session, changed);
        }
        await db.SaveChangesAsync();

        var processor = new WhatsAppWebhookProcessor(db, Mock.Of<IClock>(x => x.UtcNow == DateTime.UtcNow),
            cloud.Object, Mock.Of<IWhatsAppNotificationService>(), Mock.Of<IFirebaseGcsStorage>(), Options.Create(new FirebaseStorageOptions()),
            Options.Create(new WhatsAppAiOrchestratorOptions()), NullLogger<WhatsAppWebhookProcessor>.Instance,
            new WhatsAppAttentionService(), Mock.Of<IWhatsAppAiWorkQueue>(), Mock.Of<IWhatsAppAutomaticMessageSender>(),
            Mock.Of<IBranchBusinessHoursService>(), null!, null!, TestTenantContext.Default);
        var replyType = count <= 3 ? "button_reply" : "list_reply";
        var payload = JsonSerializer.Serialize(new
        {
            entry = new[] { new { changes = new[] { new { field = "messages", value = new
            {
                metadata = new { phone_number_id = "phone" },
                messages = new[] { new {
                    from = recipient, id = "chosen-test", type = "interactive",
                    context = new { id = stale == "old_context" ? "another-prompt" : "prompt-1" },
                    interactive = new Dictionary<string, object> { ["type"] = replyType,
                        [replyType] = new { id = clickedOption, title = "Dirección 1" } }
                } }
            } } } } }
        });
        await processor.ProcessAsync(new WhatsAppWebhookEvent(), payload, default);
        await processor.ProcessAsync(new WhatsAppWebhookEvent(), payload, default);
        var updated = WhatsAppCommercialDraft.Read(session);
        Assert.Equal(stale is null ? 1 : stale == "manual_address" ? 2 : (int?)null, updated.AddressId);
        Assert.Equal(stale is null ? 3500 : (int?)null, updated.DeliveryFee);
        Assert.Equal(saved.Version + (stale is null ? 1 : 0), session.Version);
        Assert.Equal("Conservar este pedido", updated.Notes);
        using var editor = JsonDocument.Parse(session.EditorJson!);
        Assert.Equal(41, editor.RootElement.GetProperty("orderItems")[0].GetInt32());
        if (stale is null) Assert.Equal("Calle 1 # 1-2", editor.RootElement.GetProperty("addressDescription").GetString());
        Assert.Single(db.WhatsAppMessages.Where(x => x.Direction == WhatsAppMessageDirection.Inbound));
        Assert.Empty(db.Orders);
    }

    [Theory]
    [InlineData(false, null, 1)]
    [InlineData(true, 2, 2)]
    [InlineData(true, null, 0)]
    public async Task New_order_defaults_to_the_branch_attending_the_conversation(bool central, int? operationalBranchId, int expected)
    {
        await using var db = CreateDb();
        await SeedCentralConversationAsync(db, operationalBranchId, 7);
        if (!central)
        {
            (await db.WhatsAppConversations.SingleAsync()).ChannelSettingId = null;
            db.WhatsAppBranchSettings.Add(new WhatsAppBranchSetting { BranchId = 1, PhoneNumberId = "branch", IsActive = true, IsVerified = true });
            await db.SaveChangesAsync();
        }
        var started = DraftPayload(await CreateController(db).StartCommercialDraft(11, new(), default));
        Assert.Equal(expected, started.Order.BranchId);
    }

    [Fact]
    public async Task Older_branch_draft_without_destination_can_be_loaded_and_quoted_for_its_branch()
    {
        await using var db = CreateDb();
        await SeedCentralConversationAsync(db, null, 7);
        (await db.WhatsAppConversations.SingleAsync()).ChannelSettingId = null;
        db.WhatsAppBranchSettings.Add(new WhatsAppBranchSetting { BranchId = 1, PhoneNumberId = "branch", IsActive = true, IsVerified = true });
        await db.SaveChangesAsync();
        var controller = CreateController(db);
        var started = DraftPayload(await controller.StartCommercialDraft(11, new(), default));
        var stored = await db.WhatsAppCommerceSessions.SingleAsync();
        WhatsAppCommercialDraft.Write(stored, new() { Type = OrderType.Delivery });
        await db.SaveChangesAsync();

        var loaded = DraftPayload(await controller.GetCommercialDraft(11, default));
        Assert.Equal(started.AttemptId, loaded.AttemptId);
        Assert.Equal(1, loaded.Order.BranchId);
        var quoted = await controller.CommercialDraftAction(11, "quote",
            new() { SessionId = loaded.SessionId, ExpectedVersion = loaded.Version }, null!, default);
        var quote = Assert.IsType<ApiResponse<WhatsAppDraftQuoteDto>>(Assert.IsType<OkObjectResult>(quoted).Value).Data!;
        Assert.DoesNotContain("Selecciona la sucursal que prepara.", quote.Errors);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(3, true)]
    public async Task Remote_quote_uses_shared_catalog_and_destination_inventory(int destinationStock, bool available)
    {
        await using var db = CreateDb();
        await SeedCentralConversationAsync(db, 1, 7);
        await SeedCommercialCatalogAsync(db);
        var category = await db.ProductCategories.SingleAsync();
        category.BranchId = 1;
        var product = await db.Products.SingleAsync();
        product.InventoryEnabled = true;
        product.InventoryControlMode = InventoryControlMode.Strict;
        db.AddRange(new Expense { Id = 1, Name = "Arroz", TracksInventory = true, InventoryActive = true },
            new ProductExpenseRequirement { ProductId = product.Id, ExpenseId = 1, BaseQuantity = 1 },
            new InventoryBalance { BranchId = 1, ExpenseId = 1, QuantityOnHand = 100 },
            new InventoryBalance { BranchId = 2, ExpenseId = 1, QuantityOnHand = destinationStock });
        await db.SaveChangesAsync();
        var inventory = new SenorArroz.Application.Features.Inventory.Services.InventoryService(db,
            Mock.Of<ICurrentUser>(x => x.Id == 7 && x.Role == "Cashier" && x.BranchId == 1),
            TestTenantContext.Default, Mock.Of<IClock>(x => x.UtcNow == DateTime.UtcNow),
            Mock.Of<IBranchContext>(x => x.EffectiveBranchId == 1));
        var controller = CreateController(db, inventory: inventory);
        var started = DraftPayload(await controller.StartCommercialDraft(11, new(), default));
        var saved = DraftPayload(await controller.SaveCommercialDraft(11, new()
        {
            SessionId = started.SessionId, ExpectedVersion = started.Version,
            Order = new() { BranchId = 2, GuestName = "Recoge en destino", Type = OrderType.Onsite,
                OrderDetails = [new() { ProductId = product.Id, Quantity = 2, UnitPrice = product.Price }] }
        }, default));
        var result = await controller.CommercialDraftAction(11, "quote", new()
            { SessionId = saved.SessionId, ExpectedVersion = saved.Version }, null!, default);
        var quote = Assert.IsType<ApiResponse<WhatsAppDraftQuoteDto>>(Assert.IsType<OkObjectResult>(result).Value).Data!;
        Assert.Equal(available, quote.Errors.Count == 0);
        Assert.Equal(20000, quote.Total);
        Assert.Empty(db.Orders);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Shared_draft_retains_remote_destination_and_all_editor_data_across_attention_transfer(bool central)
    {
        await using var db = CreateDb();
        await SeedCentralConversationAsync(db, 1, 7);
        var conversation = await db.WhatsAppConversations.SingleAsync();
        if (!central)
        {
            conversation.ChannelSettingId = null;
            db.WhatsAppBranchSettings.Add(new WhatsAppBranchSetting { BranchId = 1, PhoneNumberId = "branch", IsActive = true, IsVerified = true });
        }
        await SeedCommercialCatalogAsync(db);
        var controller = CreateController(db);
        var started = DraftPayload(await controller.StartCommercialDraft(11, new(), default));
        var request = new WhatsAppDraftWriteDto
        {
            SessionId = started.SessionId, ExpectedVersion = started.Version, AttentionVersion = 0,
            Order = new() { BranchId = 2, GuestName = "Recoge remoto", Type = OrderType.Reservation,
                ReservedFor = DateTime.UtcNow.AddDays(1), PrepareAt = DateTime.UtcNow.AddDays(1).AddHours(-1),
                Notes = "Sin cubiertos", OrderDetails = [new() { ProductId = 31, Quantity = 2, UnitPrice = 10000, Notes = "Sin cebolla" }],
                BankPayments = [new() { BankId = 41, Amount = 5000 }], FreeDeliveryRequested = true,
                AppliedBenefitType = OrderBenefitType.Manual, ManualBenefitReason = "Cortesía aprobada" },
            Editor = JsonSerializer.SerializeToElement(new { customerName = "Cliente", billingRecipient = new { name = "Facturación" }, tabId = "editor-a" })
        };
        var saved = DraftPayload(await controller.SaveCommercialDraft(11, request, default));
        Assert.Equal(2, saved.Order.BranchId);
        Assert.Equal(1, conversation.OperationalBranchId);
        Assert.Equal(1, conversation.BranchId);
        Assert.Equal(7, conversation.AssignedUserId);
        Assert.Equal("Sin cebolla", Assert.Single(saved.Order.OrderDetails).Notes);
        Assert.Equal(5000, Assert.Single(saved.Order.BankPayments).Amount);
        Assert.Equal("Facturación", saved.Editor!.Value.GetProperty("billingRecipient").GetProperty("name").GetString());
        Assert.IsType<ConflictObjectResult>(await controller.SaveCommercialDraft(11, request, default));
        conversation.AssignedUserId = 8;
        conversation.AttentionRevision++;
        await db.SaveChangesAsync();
        request.ExpectedVersion = saved.Version;
        Assert.IsType<ConflictObjectResult>(await controller.SaveCommercialDraft(11, request, default));
        request.AttentionVersion = 1;
        var restored = DraftPayload(await CreateController(db, 8).GetCommercialDraft(11, default));
        Assert.Equal(saved.AttemptId, restored.AttemptId);
        Assert.Equal(2, restored.Order.BranchId);
        Assert.Empty(db.Orders);
    }

    [Fact]
    public async Task Shared_confirmation_creates_one_order_without_queueing_a_whatsapp_message()
    {
        await using var db = CreateDb();
        await SeedCentralConversationAsync(db, 1, 7);
        await SeedCommercialCatalogAsync(db);
        var conversation = await db.WhatsAppConversations.SingleAsync();
        conversation.NeedsHumanResponse = true;
        await db.SaveChangesAsync();
        var controller = CreateController(db);
        var started = DraftPayload(await controller.StartCommercialDraft(11, new(), default));
        var saved = DraftPayload(await controller.SaveCommercialDraft(11, new()
        {
            SessionId = started.SessionId, ExpectedVersion = started.Version,
            Order = new() { BranchId = 2, GuestName = "Cliente remoto", Type = OrderType.Onsite,
                OrderDetails = [new() { ProductId = 31, Quantity = 2, UnitPrice = 10000 }] }
        }, default));
        var sender = CommercialSender(db, Mock.Of<IClock>(x => x.UtcNow == DateTime.UtcNow));
        var request = new WhatsAppDraftWriteDto { SessionId = saved.SessionId, ExpectedVersion = saved.Version };
        var confirmed = DraftPayload(await controller.CommercialDraftAction(11, "confirm", request, sender.Object, default));
        var replay = DraftPayload(await controller.CommercialDraftAction(11, "confirm", request, sender.Object, default));
        Assert.Equal(confirmed.OrderId, replay.OrderId);
        var order = Assert.Single(db.Orders);
        Assert.Equal("whatsapp", order.OrderSource);
        Assert.Equal(2, order.BranchId);
        Assert.Equal(1, order.OriginBranchId);
        Assert.Equal(7, order.TakenById);
        Assert.Equal(saved.AttemptId.ToString("D"), order.PosCreationKey);
        Assert.Equal("order_taken", confirmed.CommercialStatus);
        Assert.Null(confirmed.ConfirmationStatus);
        Assert.Empty(db.WhatsAppCommerceOutboxMessages);
        Assert.Empty(db.WhatsAppMessages);
        Assert.True(conversation.NeedsHumanResponse);
        sender.Verify(x => x.Send(It.IsAny<CreateOrderCommand>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("Pedido #71 confirmado. Total: $20,000. Seguimos atendiéndote por este chat.", false)]
    [InlineData("Pedido #71 confirmado por $20.000. La sede asignada continuará contigo por este chat.", true)]
    public async Task Only_self_service_confirmations_remain_visible_and_retryable(string body, bool selfService)
    {
        await using var db = CreateDb();
        await SeedCentralConversationAsync(db, 1, 7);
        var controller = CreateController(db);
        var started = DraftPayload(await controller.StartCommercialDraft(11, new(), default));
        var session = await db.WhatsAppCommerceSessions.SingleAsync();
        session.Status = "completed";
        session.OrderId = 71;
        var message = new WhatsAppCommerceOutboxMessage
        {
            ConversationId = 11, EventKey = "whatsapp-order-created:71", Body = body,
            Status = "failed", LastError = "provider_rejected_retryable"
        };
        db.WhatsAppCommerceOutboxMessages.Add(message);
        await db.SaveChangesAsync();

        var loaded = DraftPayload(await controller.GetCommercialDraft(11, default));
        Assert.Equal(selfService ? "failed" : null, loaded.ConfirmationStatus);
        var request = new WhatsAppDraftWriteDto { SessionId = started.SessionId, ExpectedVersion = loaded.Version };
        if (selfService)
            Assert.Equal("pending", DraftPayload(await controller.CommercialDraftAction(11, "retry-message", request, null!, default)).ConfirmationStatus);
        else
            await Assert.ThrowsAsync<BusinessException>(() => controller.CommercialDraftAction(11, "retry-message", request, null!, default));
        Assert.Equal(selfService ? "pending" : "failed", message.Status);
    }

    [Fact]
    public async Task Internal_notes_never_become_messages_and_new_attempt_preserves_cancelled_history()
    {
        await using var db = CreateDb();
        await SeedCentralConversationAsync(db, 1, 7);
        var controller = CreateController(db);
        var started = DraftPayload(await controller.StartCommercialDraft(11, new(), default));
        var sender = new Mock<ISender>();
        var request = new WhatsAppDraftWriteDto { SessionId = started.SessionId, ExpectedVersion = started.Version, Note = "Solo asesores" };
        var noted = DraftPayload(await controller.CommercialDraftAction(11, "note", request, sender.Object, default));
        Assert.Equal("Solo asesores", Assert.Single(noted.InternalNotes).Text);
        Assert.Empty(db.WhatsAppMessages);
        Assert.Empty(db.WhatsAppCommerceOutboxMessages);
        request.ExpectedVersion = noted.Version;
        var cancelled = DraftPayload(await controller.CommercialDraftAction(11, "cancel", request, sender.Object, default));
        request.ExpectedVersion = cancelled.Version;
        var next = DraftPayload(await controller.StartCommercialDraft(11, request, default));
        Assert.NotEqual(started.AttemptId, next.AttemptId);
        Assert.Equal(2, await db.WhatsAppCommerceSessions.CountAsync());
        Assert.Equal("no_sale", (await db.WhatsAppCommerceSessions.SingleAsync(x => x.Id == started.SessionId)).CommercialStatus);
        Assert.Empty(next.InternalNotes);
    }

    [Fact]
    public async Task Destination_coverage_and_payment_changes_require_review_and_direct_order_creation_cannot_bypass_shared_revision()
    {
        await using var db = CreateDb();
        await SeedCentralConversationAsync(db, 1, 7);
        await SeedCommercialCatalogAsync(db);
        var controller = CreateController(db);
        var started = DraftPayload(await controller.StartCommercialDraft(11, new(), default));
        var dto = new CreateOrderDto { BranchId = 2, WhatsAppConversationId = 11, Type = OrderType.Onsite, GuestName = "Cliente",
            OrderDetails = [new() { ProductId = 31, Quantity = 1, UnitPrice = 10000 }], BankPayments = [new() { BankId = 99, Amount = 5000 }] };
        var saved = DraftPayload(await controller.SaveCommercialDraft(11, new() { SessionId = started.SessionId, ExpectedVersion = started.Version, Order = dto }, default));
        var sender = CommercialSender(db, Mock.Of<IClock>(x => x.UtcNow == DateTime.UtcNow));
        var request = new WhatsAppDraftWriteDto { SessionId = saved.SessionId, ExpectedVersion = saved.Version };
        var quote = Assert.IsType<ApiResponse<WhatsAppDraftQuoteDto>>(Assert.IsType<OkObjectResult>(await controller.CommercialDraftAction(11, "quote", request, sender.Object, default)).Value).Data!;
        Assert.Contains(quote.Errors, x => x.Contains("bancos"));
        await Assert.ThrowsAsync<BusinessException>(() => controller.CommercialDraftAction(11, "confirm", request, sender.Object, default));
        await Assert.ThrowsAsync<BusinessException>(() => CreateOrderHandler(db, 7, 1).Handle(new() { Order = dto }, default));
        Assert.Empty(db.Orders);
    }

    [Fact]
    public async Task Shared_confirmation_revalidates_benefit_expiry_after_review_without_creating_an_order()
    {
        await using var db = CreateDb(); await SeedCentralConversationAsync(db, 1, 7); await SeedCommercialCatalogAsync(db);
        var promotion = new DailyPromotion { Id = 51, BranchId = 2, Type = DailyPromotionType.PercentageDiscount,
            DiscountPercentage = 10, IsActive = true, StartsAt = DateTime.UtcNow.AddDays(-1), EndsAt = DateTime.UtcNow.AddHours(1) };
        db.DailyPromotions.Add(promotion); await db.SaveChangesAsync();
        var controller = CreateController(db);
        var started = DraftPayload(await controller.StartCommercialDraft(11, new(), default));
        var saved = DraftPayload(await controller.SaveCommercialDraft(11, new()
        {
            SessionId = started.SessionId, ExpectedVersion = started.Version,
            Order = new() { BranchId = 2, Type = OrderType.Onsite, GuestName = "Recoge", AppliedBenefitType = OrderBenefitType.DailyPromotion,
                AppliedBenefitSourceId = 51, OrderDetails = [new() { ProductId = 31, Quantity = 1, UnitPrice = 10000, Discount = 1000 }] }
        }, default));
        var sender = CommercialSender(db, Mock.Of<IClock>(x => x.UtcNow == DateTime.UtcNow));
        var request = new WhatsAppDraftWriteDto { SessionId = saved.SessionId, ExpectedVersion = saved.Version };
        var quoted = Assert.IsType<ApiResponse<WhatsAppDraftQuoteDto>>(Assert.IsType<OkObjectResult>(await controller.CommercialDraftAction(11, "quote", request, sender.Object, default)).Value).Data!;
        Assert.Empty(quoted.Errors);
        promotion.IsActive = false; await db.SaveChangesAsync();
        request.ReviewedFingerprint = quoted.Fingerprint;
        await Assert.ThrowsAsync<BusinessException>(() => controller.CommercialDraftAction(11, "confirm", request, sender.Object, default));
        Assert.Empty(db.Orders); Assert.Empty(db.WhatsAppCommerceOutboxMessages);
        sender.Verify(x => x.Send(It.IsAny<CreateOrderCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static WhatsAppCommercialDraftDto DraftPayload(IActionResult result) =>
        Assert.IsType<ApiResponse<WhatsAppCommercialDraftDto>>(Assert.IsType<OkObjectResult>(result).Value).Data!;

    private static async Task SeedCommercialCatalogAsync(SenorArroz.Infrastructure.Data.ApplicationDbContext db)
    {
        db.Users.Add(new User { Id = 7, BranchId = 1, Name = "Asesor", Email = "local@example.invalid", Role = UserRole.Cashier });
        db.ProductCategories.Add(new ProductCategory { Id = 30, BranchId = 2, Name = "Arroces" });
        db.Products.Add(new Product { Id = 31, CategoryId = 30, Name = "Arroz", Price = 10000, Active = true });
        db.Banks.Add(new Bank { Id = 41, BranchId = 2, Name = "Banco destino" });
        await db.SaveChangesAsync();
    }

    internal static Mock<ISender> CommercialSender(SenorArroz.Infrastructure.Data.ApplicationDbContext db, IClock clock, int actor = 7)
    {
        var mapper = new MapperConfiguration(config => config.AddMaps(typeof(CreateOrderCommand).Assembly), NullLoggerFactory.Instance).CreateMapper();
        var handler = new CreateOrderHandler(new OrderRepository(db, clock), db, mapper,
            Mock.Of<ICurrentUser>(x => x.Id == actor && x.Role == "Cashier" && x.BranchId == 1), Mock.Of<IOrderNotificationService>(), clock,
            branchContext: Mock.Of<IBranchContext>(x => x.EffectiveBranchId == 1));
        var sender = new Mock<ISender>();
        sender.Setup(x => x.Send(It.IsAny<CreateOrderCommand>(), It.IsAny<CancellationToken>()))
            .Returns((CreateOrderCommand command, CancellationToken ct) => handler.Handle(command, ct));
        return sender;
    }
}
