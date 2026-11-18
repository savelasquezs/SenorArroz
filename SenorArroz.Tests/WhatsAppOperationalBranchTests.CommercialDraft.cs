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
    [InlineData("Arroz Paisa", 5, false)]
    [InlineData("Papas a la francesa", 2, false)]
    [InlineData("Yuca", 2, false)]
    [InlineData("Arroz Paisa", 5, true)]
    public async Task Product_choices_use_catalog_prices_and_apply_once_to_the_active_draft(string name, int count, bool priceChanged)
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
        db.ProductCategories.Add(new ProductCategory { Id = 4, BranchId = 1, Name = name, StorefrontRole = isRice ? "rice" : "addition" });
        for (var i = 1; i <= count; i++)
            db.Products.Add(new Product { Id = i, CategoryId = 4, CommercialProfileId = groupId,
                Name = $"{name} {i}", StorefrontVariantLabel = $"Tamaño {i}", Price = 5000 * i,
                ServesPeopleMin = isRice ? i : null, ServesPeopleMax = isRice ? i + 1 : null });
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
            .ReturnsAsync((string _, string _, string _, string _, IReadOnlyList<WhatsAppReplyButton> buttons, CancellationToken _) =>
            {
                Assert.False(isRice);
                Assert.Equal(2, buttons.Count);
                Assert.Equal("Tamaño 2 · $10.000", buttons[1].Title);
                optionId = buttons[1].Id;
                return new(true, "product-prompt", null);
            });
        var controller = CreateController(db, cloud: cloud.Object);
        var groups = Assert.IsType<ApiResponse<WhatsAppProductChoiceGroupDto[]>>(
            Assert.IsType<OkObjectResult>(await controller.GetCommercialProductGroups(default)).Value).Data;
        Assert.Equal(name, Assert.Single(groups!).Name);
        var draft = DraftPayload(await controller.StartCommercialDraft(11, new(), default));
        var saved = DraftPayload(await controller.SaveCommercialDraft(11, new()
        {
            SessionId = draft.SessionId, ExpectedVersion = draft.Version,
            Order = new() { BranchId = 2, Type = OrderType.Delivery, Notes = "Mantener nota del cajero",
                OrderDetails = [new() { ProductId = 1, Quantity = 3, UnitPrice = 5000 }] },
            Editor = JsonSerializer.SerializeToElement(new { orderItems = new[] { new { productId = 1, quantity = 3 } } })
        }, default));
        var sent = await controller.SendCommercialProductChoices(11, groupId,
            new() { SessionId = saved.SessionId, ExpectedVersion = saved.Version }, default);
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
            Assert.Equal((2, 1, 10000), (chosen.ProductId, chosen.Quantity, chosen.UnitPrice));
            Assert.Equal($"{name} 2", chosen.WhatsAppProductChoice?.Name);
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
