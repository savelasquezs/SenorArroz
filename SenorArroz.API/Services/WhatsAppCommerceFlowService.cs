using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SenorArroz.API.Controllers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Services;

public sealed class WhatsAppCommerceFlowService(
    IApplicationDbContext db,
    IWhatsAppCloudClient cloud,
    IClock clock,
    IOptions<WhatsAppFlowOptions> options,
    StorefrontCommerceService storefront,
    StorefrontCustomerAuthService customerAuth,
    IMapper mapper,
    IOrderNotificationService notifications,
    ILogger<PublicStorefrontController> storefrontLogger,
    ILogger<WhatsAppCommerceFlowService> logger,
    WhatsAppFlowImageService? images = null,
    IWompiPaymentService? wompi = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly WhatsAppFlowOptions _options = options.Value;

    public static bool IsPurchaseIntent(string? text)
    {
        var withoutDiacritics = new string((text ?? string.Empty).Trim().ToLowerInvariant()
            .Normalize(NormalizationForm.FormD)
            .Where(x => CharUnicodeInfo.GetUnicodeCategory(x) != UnicodeCategory.NonSpacingMark)
            .ToArray())
            .Normalize(NormalizationForm.FormC);
        var normalized = string.Join(' ', withoutDiacritics.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return normalized is "pedido" or "pedir" or "comprar" or "hacer pedido" or "ver menu";
    }

    public static bool IsPaymentRetryIntent(string? text) =>
        string.Equals(text?.Trim(), "reintentar pago", StringComparison.OrdinalIgnoreCase);

    public async Task<bool> RetryPaymentAsync(int conversationId, int incomingMessageId, CancellationToken ct)
    {
        if (!_options.Enabled || _options.TenantId != 1 || wompi is null) return false;
        var conversation = await db.WhatsAppConversations.Include(x => x.ChannelSetting).FirstOrDefaultAsync(
            x => x.Id == conversationId && x.TenantId == 1 && x.ChannelSettingId != null, ct);
        if (conversation?.ChannelSetting is not { IsActive: true, IsVerified: true, FlowEnabled: true } channel) return false;
        var phone = ColombianMobilePhone.Normalize(conversation.PhoneNumber);
        if (!ColombianMobilePhone.IsValid(phone)
            || _options.RestrictToAllowlist && !_options.AllowedPhoneHashes.Contains(Sha256(phone), StringComparer.OrdinalIgnoreCase)) return false;
        var eventKey = $"whatsapp-payment-retry:{incomingMessageId}";
        if (await db.WhatsAppCommerceOutboxMessages.AnyAsync(x => x.EventKey == eventKey, ct)) return true;
        await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        var checkout = await db.StorefrontCheckouts.Where(x => x.TenantId == 1 && x.WhatsAppConversationId == conversationId
                && x.OrderSource == "whatsapp_flow" && x.CustomerPhone == phone)
            .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        WompiCheckoutData? payment = null;
        var body = "No hay un checkout vigente para reintentar. Escribe “pedido” para comenzar de nuevo o solicita un asesor.";
        if (checkout is not null && checkout.ExpiresAt > clock.UtcNow && checkout.OrderId is null && checkout.Status != "review_required")
        {
            try
            {
                payment = await wompi.RetryCheckoutAsync(1, checkout, clock.UtcNow, ct);
                body = "Este enlace permite reintentar el pago dentro del plazo original de tu pedido. Te confirmaremos la aprobación por este chat.";
            }
            catch (BusinessException)
            {
                body = "No fue posible habilitar el reintento. Solicita un asesor para revisar el estado del pago.";
            }
        }
        db.WhatsAppCommerceOutboxMessages.Add(new WhatsAppCommerceOutboxMessage
        {
            TenantId = 1, ChannelSettingId = channel.Id, ConversationId = conversationId, EventKey = eventKey,
            Body = body, Url = payment is null ? null : BuildWompiUrl(payment),
            ButtonText = payment is null ? null : "Reintentar pago", NextAttemptAt = clock.UtcNow
        });
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return true;
    }

    public async Task<bool> StartAsync(int conversationId, int channelSettingId, CancellationToken ct)
    {
        if (!_options.Enabled || _options.TenantId != 1)
            return false;

        var channel = await db.WhatsAppChannelSettings.AsNoTracking().FirstOrDefaultAsync(
            x => x.Id == channelSettingId && x.TenantId == 1 && x.IsActive && x.IsVerified && x.FlowEnabled, ct);
        if (channel is null || string.IsNullOrWhiteSpace(channel.FlowId))
            return false;

        var conversation = await db.WhatsAppConversations.FirstOrDefaultAsync(
            x => x.Id == conversationId && x.TenantId == 1 && x.ChannelSettingId == channel.Id, ct);
        if (conversation is null || conversation.AttentionMode is WhatsAppAttentionMode.Human or WhatsAppAttentionMode.WaitingForHuman or WhatsAppAttentionMode.Paused)
            return false;
        var phone = ColombianMobilePhone.Normalize(conversation.PhoneNumber);
        if (!ColombianMobilePhone.IsValid(phone)
            || _options.RestrictToAllowlist && !_options.AllowedPhoneHashes.Contains(Sha256(phone), StringComparer.OrdinalIgnoreCase))
            return false;

        var recipient = WhatsAppRecipientResolver.Resolve(conversation);
        if (recipient is null)
            return false;

        var customerSession = await customerAuth.ResolveTrustedPhoneAsync(conversation.PhoneNumber ?? recipient, ct);
        var previous = await db.WhatsAppCommerceSessions
            .Where(x => x.ConversationId == conversation.Id && x.Status == "active" && x.ExpiresAt > clock.UtcNow)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);
        var state = previous is null
            ? new WhatsAppCommerceState
            {
                Name = customerSession.Customer?.Name ?? conversation.ContactName ?? conversation.WhatsAppUsername ?? string.Empty,
                AmbiguousCustomer = customerSession.AmbiguousCustomer,
                LastScreen = "FULFILLMENT"
            }
            : DeserializeState(previous.StateJson);
        if (previous is not null)
        {
            previous.Status = "abandoned";
            TrackEvent(previous, "flow_abandoned", previous.BranchId, null, "restart");
        }

        var rawToken = Base64Url(RandomNumberGenerator.GetBytes(32));
        var session = new WhatsAppCommerceSession
        {
            TenantId = 1,
            ChannelSettingId = channel.Id,
            ConversationId = conversation.Id,
            CustomerId = customerSession.Customer?.Id,
            BranchId = conversation.OperationalBranchId,
            FlowTokenHash = Sha256(rawToken),
            StateJson = JsonSerializer.Serialize(state, JsonOptions),
            IdempotencyKey = $"waf_{Guid.NewGuid():N}",
            ExpiresAt = clock.UtcNow.AddMinutes(Math.Clamp(_options.SessionLifetimeMinutes, 15, 120))
        };
        db.WhatsAppCommerceSessions.Add(session);
        TrackEvent(session, "flow_started", session.BranchId, state.LastScreen);
        await db.SaveChangesAsync(ct);

        var sent = await cloud.SendFlowMessageAsync(
            channel.PhoneNumberId, channel.AccessToken, recipient,
            previous is null ? "Haz tu pedido completo sin salir de WhatsApp." : "Tu carrito sigue disponible. Puedes continuar donde quedaste.",
            previous is null ? "Ver menú" : "Continuar pedido", channel.FlowId, rawToken, state.LastScreen, ct);
        if (!sent.Success)
        {
            session.Status = "abandoned";
            TrackEvent(session, "flow_send_failed", session.BranchId, state.LastScreen);
            await db.SaveChangesAsync(ct);
            logger.LogWarning("WhatsApp Flow send failed. ChannelId={ChannelId} ConversationId={ConversationId}", channel.Id, conversation.Id);
            return false;
        }

        db.WhatsAppMessages.Add(new WhatsAppMessage
        {
            ConversationId = conversation.Id,
            WhatsAppMessageId = sent.WhatsAppMessageId,
            Direction = WhatsAppMessageDirection.Outbound,
            Type = WhatsAppMessageType.Text,
            TextBody = "Menú interactivo enviado",
            Status = WhatsAppMessageStatus.Sent,
            Timestamp = clock.UtcNow,
            SentByAi = false,
            RawPayload = JsonSerializer.Serialize(new { origin = "whatsapp_flow", sessionId = session.Id })
        });
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<WhatsAppCommerceSession?> FindSessionAsync(int channelId, string flowToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(flowToken)) return null;
        var tokenHash = Sha256(flowToken);
        return await db.WhatsAppCommerceSessions
            .Include(x => x.Conversation)
            .FirstOrDefaultAsync(x => x.ChannelSettingId == channelId && x.FlowTokenHash == tokenHash, ct);
    }

    public async Task<Dictionary<string, object?>> HandleAsync(
        WhatsAppCommerceSession session,
        string action,
        string? screen,
        string flowToken,
        JsonElement data,
        CancellationToken ct)
    {
        if (session.TenantId != 1 || session.ExpiresAt <= clock.UtcNow)
        {
            session.Status = "expired";
            TrackEvent(session, "flow_expired", session.BranchId, screen);
            await db.SaveChangesAsync(ct);
            return Error("FULFILLMENT", "La sesión venció. Escribe “pedido” para comenzar nuevamente.", session.Version);
        }
        if (session.Status != "active")
            return Complete(flowToken, "Esta sesión ya fue procesada. Revisa los mensajes de este chat.");

        var state = DeserializeState(session.StateJson);
        if (action == "ping")
            return new() { ["data"] = new Dictionary<string, object?> { ["status"] = "active" } };
        if (action == "INIT")
            return await BuildScreenAsync(session, state, state.LastScreen, null, ct);
        if (action != "data_exchange")
            return Error(state.LastScreen, "Acción no soportada.", session.Version);

        var requestedVersion = GetInt(data, "_session_version");
        if (requestedVersion != session.Version)
            return await BuildScreenAsync(session, state, state.LastScreen, "El pedido cambió en otra pantalla. Revisa los datos actualizados.", ct);

        var current = string.IsNullOrWhiteSpace(screen) ? state.LastScreen : screen.ToUpperInvariant();
        if (current != state.LastScreen)
            return await BuildScreenAsync(session, state, state.LastScreen, "Continúa desde la pantalla vigente del pedido.", ct);
        var command = GetString(data, "command");
        if (command == "human")
        {
            session.Conversation.AttentionMode = WhatsAppAttentionMode.WaitingForHuman;
            session.Conversation.AttentionModeUpdatedAt = clock.UtcNow;
            session.Status = "completed";
            session.CompletedAt = clock.UtcNow;
            TrackEvent(session, "human_transfer", session.BranchId, current);
            await db.SaveChangesAsync(ct);
            return Complete(flowToken, "Un asesor continuará contigo por este chat.");
        }

        string next;
        string? error = null;
        switch (current)
        {
            case "FULFILLMENT":
                state.FulfillmentType = GetString(data, "fulfillment_type");
                if (state.FulfillmentType is not ("delivery" or "pickup"))
                    error = "Selecciona domicilio o recogida.";
                next = error is null ? "ADDRESS_PICKUP" : current;
                break;
            case "ADDRESS_PICKUP":
                state.Name = GetString(data, "name") ?? state.Name;
                if (state.Name.Length is < 2 or > 100)
                {
                    error = "Escribe el nombre de quien recibe (2 a 100 caracteres).";
                    next = current;
                    break;
                }
                if (state.AmbiguousCustomer)
                {
                    error = "Este número corresponde a varios clientes. Solicita un asesor para proteger tus datos.";
                    next = current;
                    break;
                }
                if (state.FulfillmentType == "pickup")
                {
                    state.SelectedBranchId = GetInt(data, "branch_id");
                    state.SavedAddressId = null;
                    if (!state.SelectedBranchId.HasValue || !await db.Branches.AnyAsync(
                        x => x.Id == state.SelectedBranchId && x.IsActive && x.StorefrontTakenByUserId != null
                            && db.Users.Any(u => u.Id == x.StorefrontTakenByUserId && u.BranchId == x.Id && u.Active), ct))
                        error = "Selecciona una sede disponible.";
                    else session.Conversation.OperationalBranchId = state.SelectedBranchId;
                }
                else
                {
                    state.SavedAddressId = GetInt(data, "saved_address_id");
                    if (!state.SavedAddressId.HasValue)
                    {
                        state.City = GetString(data, "city");
                        state.Address = GetString(data, "address");
                        state.AddressAdditionalInfo = GetString(data, "address_additional_info");
                        state.AddressLabel = GetString(data, "address_label");
                        if (string.IsNullOrWhiteSpace(state.City) || string.IsNullOrWhiteSpace(state.Address))
                            error = "Completa ciudad y dirección.";
                        else
                            error = await ResolveNewAddressAsync(state, GetBool(data, "address_confirmed"), ct);
                    }
                }
                next = error is null ? "CATEGORY" : current;
                break;
            case "CATEGORY":
                state.Category = GetString(data, "category");
                state.ProductPage = 0;
                if (state.Category is not ("rice" or "combo" or "beverage" or "addition")) error = "Selecciona una categoría.";
                next = error is null ? "PRODUCTS" : current;
                break;
            case "PRODUCTS":
                if (command is "next_page" or "previous_page" or "catalog")
                {
                    var productCount = FlattenCatalog(await GetCatalogAsync(ct), state.Category).Count();
                    state.ProductPage = Math.Clamp(state.ProductPage + (command == "next_page" ? 1 : -1), 0, Math.Max(0, (productCount - 1) / 6));
                    next = command == "catalog" ? "CATEGORY" : current;
                    break;
                }
                var productId = GetInt(data, "product_id");
                var quantity = GetInt(data, "quantity");
                var selectedProduct = FlattenCatalog(await GetCatalogAsync(ct), state.Category).FirstOrDefault(x => x.ProductId == productId);
                if (selectedProduct is null || selectedProduct.AvailabilityStatus == "unavailable")
                    error = "Selecciona un producto disponible.";
                else if (quantity is null or < 1 or > 50)
                    error = "La cantidad debe ser un entero de 1 a 50.";
                else if (!AddOrReplace(state.Cart, selectedProduct.ProductId, quantity.Value))
                    error = "El carrito admite máximo 30 productos distintos.";
                next = error is null ? "CART" : current;
                break;
            case "CART":
                ApplyCartCommand(state, data);
                if (state.Cart.Count == 0) error = "Agrega al menos un producto.";
                next = command == "catalog" ? "CATEGORY" : command == "continue" && error is null ? "BENEFITS" : current;
                break;
            case "BENEFITS":
                state.BenefitSelection = GetString(data, "benefit_selection");
                if (state.BenefitSelection == "none") state.BenefitSelection = null;
                var benefitQuote = await TryQuoteAsync(session, state, ct);
                if (benefitQuote?.BenefitConflict == true)
                    error = "Elige entre la promoción disponible y tu premio de fidelización.";
                if (benefitQuote is null) error = "Revisa el carrito y los datos de entrega antes de continuar.";
                next = error is null ? "PAYMENT" : current;
                break;
            case "PAYMENT":
                state.PaymentMethod = GetString(data, "payment_method");
                state.OrderNotes = GetString(data, "order_notes");
                if (state.PaymentMethod is not ("cash" or "online")) error = "Selecciona un medio de pago.";
                next = error is null ? "SUMMARY" : current;
                break;
            case "SUMMARY":
                if (command == "address")
                {
                    state.FulfillmentType = "delivery";
                    state.SelectedBranchId = null;
                    next = "ADDRESS_PICKUP";
                    break;
                }
                if (command == "pickup")
                {
                    state.FulfillmentType = "pickup";
                    state.SavedAddressId = null;
                    next = "ADDRESS_PICKUP";
                    break;
                }
                if (command == "confirm") return await ConfirmAsync(session, state, flowToken, ct);
                error = "Selecciona confirmar pedido o modifica los datos.";
                next = current;
                break;
            default:
                next = "FULFILLMENT";
                break;
        }

        state.LastScreen = next;
        session.BranchId = state.SelectedBranchId;
        session.Version++;
        return await BuildScreenAsync(session, state, next, error, ct);
    }

    private async Task<Dictionary<string, object?>> BuildScreenAsync(
        WhatsAppCommerceSession session,
        WhatsAppCommerceState state,
        string screen,
        string? error,
        CancellationToken ct)
    {
        TrackEvent(session, "screen_reached", session.BranchId, screen, session.Version.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(error))
            TrackEvent(session, "validation_error", session.BranchId, screen, $"{session.Version}:validation");
        var payload = new Dictionary<string, object?>
        {
            ["_session_version"] = session.Version,
            ["error_message"] = error ?? string.Empty,
            ["name"] = state.Name,
            ["fulfillment_type"] = state.FulfillmentType ?? string.Empty
        };

        if (screen == "ADDRESS_PICKUP")
        {
            var customer = await customerAuth.ResolveTrustedPhoneAsync(session.Conversation.PhoneNumber ?? string.Empty, ct);
            state.AmbiguousCustomer = customer.AmbiguousCustomer;
            session.CustomerId = customer.Customer?.Id;
            if (customer.AmbiguousCustomer) payload["name"] = string.Empty;
            payload["ambiguous_customer"] = customer.AmbiguousCustomer;
            var savedAddresses = customer.AmbiguousCustomer ? [] : customer.Addresses.Select(x => new
            {
                id = x.Id.ToString(CultureInfo.InvariantCulture),
                title = ShortTitle(string.IsNullOrWhiteSpace(x.Label) ? "Dirección guardada" : x.Label),
                description = $"{x.Address}{(string.IsNullOrWhiteSpace(x.AdditionalInfo) ? string.Empty : $", {x.AdditionalInfo}")}"
            }).ToList();
            savedAddresses.Insert(0, new { id = "new", title = "Usar dirección nueva", description = "Completa los datos abajo" });
            payload["saved_addresses"] = savedAddresses;
            var availabilityAction = await storefront.GetBranchAvailability(ct);
            var availability = (availabilityAction.Result as OkObjectResult)?.Value as ApiResponse<IReadOnlyCollection<PublicBranchAvailabilityDto>>;
            var availableIds = availability?.Data?.Where(x => x.IsAvailable).Select(x => x.BranchId).ToArray() ?? [];
            var branches = await db.Branches.AsNoTracking().Where(x => availableIds.Contains(x.Id))
                .OrderBy(x => x.Name).Select(x => new { id = x.Id.ToString(), title = x.Name, description = x.Address }).ToListAsync(ct);
            payload["branches"] = branches.Count > 0 ? branches : [new { id = "unavailable", title = "No hay sedes disponibles", description = "Solicita un asesor" }];
            payload["cities"] = new[] { new { id = "Medellín", title = "Medellín" }, new { id = "Bello", title = "Bello" }, new { id = "Copacabana", title = "Copacabana" } };
            payload["normalized_address"] = state.FormattedAddress ?? string.Empty;
            payload["requires_address_confirmation"] = state.AddressRequiresConfirmation;
            payload["city"] = state.City ?? string.Empty;
            payload["address"] = state.Address ?? string.Empty;
            payload["address_label"] = state.AddressLabel ?? string.Empty;
            payload["address_additional_info"] = state.AddressAdditionalInfo ?? string.Empty;
            payload["is_delivery"] = state.FulfillmentType == "delivery";
            payload["is_pickup"] = state.FulfillmentType == "pickup";
        }
        else if (screen is "CATEGORY" or "PRODUCTS" or "CART" or "BENEFITS" or "SUMMARY")
        {
            var catalog = await GetCatalogAsync(ct);
            payload["categories"] = new[]
            {
                new { id = "rice", title = "Arroces" }, new { id = "combo", title = "Combos" },
                new { id = "beverage", title = "Bebidas" }, new { id = "addition", title = "Adiciones" }
            };
            if (screen == "PRODUCTS")
            {
                var products = new List<Dictionary<string, object?>>();
                var page = FlattenCatalog(catalog, state.Category).Skip(state.ProductPage * 6).Take(6).ToArray();
                var pageImages = await Task.WhenAll(page.Select(x => images is null ? Task.FromResult<string?>(null) : images.GetBase64Async(x.PhotoUrl, ct)));
                for (var index = 0; index < page.Length; index++)
                {
                    var x = page[index];
                    var product = new Dictionary<string, object?>
                    {
                        ["id"] = x.ProductId.ToString(),
                        ["title"] = ShortTitle(x.Name),
                        ["description"] = $"{x.VariantLabel} · {Money(x.Price)}{(x.AvailabilityStatus == "lowStock" ? " · Pocas unidades" : string.Empty)}",
                        ["enabled"] = x.AvailabilityStatus != "unavailable"
                    };
                    var image = pageImages[index];
                    if (image is not null) product["image"] = image;
                    products.Add(product);
                }
                if (products.Count == 0) products.Add(new() { ["id"] = "unavailable", ["title"] = "Sin productos", ["enabled"] = false });
                payload["products"] = products;
            }
            var quote = state.Cart.Count == 0 ? null : await TryQuoteAsync(session, state, ct);
            var allProducts = new[] { "rice", "combo", "beverage", "addition" }.SelectMany(category => FlattenCatalog(catalog, category)).ToArray();
            payload["cart_lines"] = state.Cart.Select(item => new
            {
                id = item.ProductId.ToString(),
                title = ShortTitle($"{item.Quantity} × {allProducts.FirstOrDefault(x => x.ProductId == item.ProductId)?.Name ?? "Producto no disponible"}"),
                description = allProducts.FirstOrDefault(x => x.ProductId == item.ProductId)?.VariantLabel ?? "Elimina este producto"
            }).ToArray();
            payload["cart_total"] = quote is null ? "$0" : Money(quote.Total);
            payload["benefits"] = new[] { new { id = "none", title = "Continuar sin elegir" } };
            payload["summary"] = "No hay una cotización válida. Revisa el carrito y la entrega.";
            if (quote is not null)
            {
                payload["benefits"] = quote.AvailableBenefits.Select(x => new { id = x.Source, title = ShortTitle(x.Title) })
                    .Prepend(new { id = "none", title = "Continuar sin elegir" }).ToArray();
                payload["summary"] = BuildSummary(quote, state.PaymentMethod);
                payload["online_payment_available"] = quote.OnlinePaymentAvailable;
                state.SelectedBranchId = quote.IsOutsideCoverage ? null : quote.CheckoutBranchId;
                session.BranchId = state.SelectedBranchId;
                session.Conversation.OperationalBranchId = state.SelectedBranchId;
                if (quote.IsOutsideCoverage)
                    payload["error_message"] = "Esta dirección está fuera de cobertura. Cambia la dirección, elige recogida o solicita un asesor.";
            }
            else if (state.Cart.Count > 0)
            {
                payload["error_message"] = "No fue posible actualizar la cotización. Revisa los datos o solicita un asesor.";
            }
        }

        state.LastScreen = screen;
        session.StateJson = JsonSerializer.Serialize(state, JsonOptions);
        await db.SaveChangesAsync(ct);
        return new() { ["screen"] = screen, ["data"] = payload };
    }

    private async Task<Dictionary<string, object?>> ConfirmAsync(WhatsAppCommerceSession session, WhatsAppCommerceState state, string flowToken, CancellationToken ct)
    {
        var customer = await customerAuth.ResolveTrustedPhoneAsync(session.Conversation.PhoneNumber ?? string.Empty, ct);
        if (customer.AmbiguousCustomer)
            return await BuildScreenAsync(session, state, "ADDRESS_PICKUP", "Este número corresponde a varios clientes. Solicita un asesor.", ct);
        var quote = await TryQuoteAsync(session, state, ct);
        if (quote is null)
            return await BuildScreenAsync(session, state, "SUMMARY", "No fue posible actualizar la cotización. Revisa los datos o solicita un asesor.", ct);
        if (quote.IsOutsideCoverage)
            return await BuildScreenAsync(session, state, "SUMMARY", "Esta dirección está fuera de cobertura. Cambia la dirección, elige recogida o solicita un asesor.", ct);
        var request = BuildOrderRequest(session, state);
        var action = await storefront.ConfirmOrderTrusted(
            request, session.IdempotencyKey, customer, mapper, notifications, storefrontLogger,
            "whatsapp_flow", session.ConversationId, ct);
        if (action.Result is not OkObjectResult { Value: ApiResponse<PublicStorefrontOrderResult> response } || response.Data is null)
        {
            var message = action.Result is ObjectResult { Value: ApiResponse<PublicStorefrontOrderResult> rejected }
                ? rejected.Message
                : "No fue posible confirmar el pedido. Revisa el resumen o solicita un asesor.";
            return await BuildScreenAsync(session, state, "SUMMARY", message, ct);
        }

        var result = response.Data;
        session.Status = "completed";
        session.CompletedAt = clock.UtcNow;
        session.BranchId = result.BranchId;
        session.Conversation.OperationalBranchId = result.BranchId;
        session.Conversation.LastMessagePreview = result.PaymentMethod == "online" ? "Enlace de pago enviado" : "Pedido confirmado";
        session.Version++;
        var body = result.OrderId.HasValue
            ? $"Pedido #{result.OrderId} confirmado por {Money(result.Total)}. La sede asignada continuará contigo por este chat."
            : $"Tu pedido por {Money(result.Total)} quedó reservado durante 15 minutos. Completa el pago para confirmarlo.";
        var eventKey = result.OrderId.HasValue ? $"whatsapp-order-created:{result.OrderId}" : $"whatsapp-checkout-created:{result.CheckoutId}";
        TrackEvent(session, result.OrderId.HasValue ? "order_created" : "checkout_created", result.BranchId, "COMPLETE", result.OrderId?.ToString(CultureInfo.InvariantCulture) ?? result.CheckoutId);
        if (!await db.WhatsAppCommerceOutboxMessages.AnyAsync(x => x.EventKey == eventKey, ct))
        {
            db.WhatsAppCommerceOutboxMessages.Add(new WhatsAppCommerceOutboxMessage
            {
                TenantId = 1,
                ChannelSettingId = session.ChannelSettingId,
                ConversationId = session.ConversationId,
                EventKey = eventKey,
                Body = body,
                ButtonText = result.WompiCheckout is null ? null : "Pagar con Wompi",
                Url = result.WompiCheckout is null ? null : BuildWompiUrl(result.WompiCheckout),
                NextAttemptAt = clock.UtcNow
            });
        }
        await db.SaveChangesAsync(ct);
        return Complete(flowToken, result.OrderId.HasValue ? $"Pedido #{result.OrderId} confirmado." : "Revisa el enlace de pago enviado al chat.");
    }

    private async Task<PublicDeliveryQuoteDto?> TryQuoteAsync(WhatsAppCommerceSession session, WhatsAppCommerceState state, CancellationToken ct)
    {
        try
        {
            var customer = await customerAuth.ResolveTrustedPhoneAsync(session.Conversation.PhoneNumber ?? string.Empty, ct);
            var action = await storefront.QuoteTrusted(BuildOrderRequest(session, state), customer, ct);
            return action.Result is OkObjectResult { Value: ApiResponse<PublicDeliveryQuoteDto> response } ? response.Data : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not DbUpdateException)
        {
            logger.LogWarning("WhatsApp Flow quote failed. SessionId={SessionId} ErrorType={ErrorType}", session.Id, ex.GetType().Name);
            return null;
        }
    }

    private PublicStorefrontOrderRequest BuildOrderRequest(WhatsAppCommerceSession session, WhatsAppCommerceState state) => new()
    {
        FulfillmentType = state.FulfillmentType ?? "delivery",
        Name = string.IsNullOrWhiteSpace(state.Name) ? "Cliente WhatsApp" : state.Name,
        Phone = ColombianMobilePhone.Normalize(session.Conversation.PhoneNumber),
        City = state.City,
        Address = state.Address,
        AddressAdditionalInfo = state.AddressAdditionalInfo,
        Latitude = state.Latitude,
        Longitude = state.Longitude,
        SelectedBranchId = state.SelectedBranchId,
        SavedAddressId = state.SavedAddressId,
        AddressLabel = state.AddressLabel,
        BenefitSelection = state.BenefitSelection,
        PaymentMethod = state.PaymentMethod ?? "cash",
        OrderNotes = state.OrderNotes,
        Items = state.Cart.Select(x => new PublicCartItemRequest { ProductId = x.ProductId, Quantity = x.Quantity }).ToList()
    };

    private async Task<string?> ResolveNewAddressAsync(WhatsAppCommerceState state, bool confirmed, CancellationToken ct)
    {
        var action = await storefront.PreviewAddress(new PublicAddressPreviewRequest { City = state.City!, Address = state.Address! }, ct);
        if (action.Result is not OkObjectResult { Value: ApiResponse<PublicAddressPreviewDto> response } || response.Data is null)
            return action.Result is ObjectResult { Value: ApiResponse<PublicAddressPreviewDto> rejected } ? rejected.Message : "No fue posible ubicar la dirección.";
        var confirmedSameAddress = confirmed && state.AddressRequiresConfirmation
            && state.FormattedAddress == response.Data.FormattedAddress
            && state.Latitude == response.Data.Latitude && state.Longitude == response.Data.Longitude;
        state.FormattedAddress = response.Data.FormattedAddress;
        state.Latitude = response.Data.Latitude;
        state.Longitude = response.Data.Longitude;
        state.AddressRequiresConfirmation = !confirmedSameAddress;
        if (state.AddressRequiresConfirmation)
            return $"Confirma la dirección encontrada: {state.FormattedAddress}";
        state.Address = state.FormattedAddress;
        return null;
    }

    private async Task<PublicCatalogDto> GetCatalogAsync(CancellationToken ct)
    {
        var action = await storefront.GetCatalog(ct);
        return ((action.Result as OkObjectResult)?.Value as ApiResponse<PublicCatalogDto>)?.Data
            ?? throw new InvalidOperationException("El catálogo no está disponible.");
    }

    private static IEnumerable<WhatsAppFlowProductOption> FlattenCatalog(PublicCatalogDto catalog, string? category) => (category switch
    {
        "combo" => catalog.ComboGroups,
        "beverage" => catalog.BeverageGroups,
        "addition" => catalog.AdditionGroups,
        _ => catalog.RiceGroups
    }).SelectMany(group => group.Options.Select(option => new WhatsAppFlowProductOption(
        option.ProductId,
        option.Name,
        option.VariantLabel,
        option.Price,
        option.AvailabilityStatus,
        category is "rice" or "combo" ? group.PhotoUrl : null)));

    private string BuildWompiUrl(WompiCheckoutData checkout)
    {
        var values = new Dictionary<string, string>
        {
            ["public-key"] = checkout.PublicKey,
            ["currency"] = checkout.Currency,
            ["amount-in-cents"] = checkout.AmountInCents.ToString(CultureInfo.InvariantCulture),
            ["reference"] = checkout.Reference,
            ["signature:integrity"] = checkout.IntegritySignature,
            ["expiration-time"] = checkout.ExpiresAt,
            ["redirect-url"] = _options.PaymentReturnUrl
        };
        return "https://checkout.wompi.co/p/?" + string.Join('&', values.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
    }

    private static string BuildSummary(PublicDeliveryQuoteDto quote, string? paymentMethod) =>
        $"{(quote.FulfillmentType == "pickup" ? "Recogida" : "Domicilio")} · {quote.Branches.FirstOrDefault(x => x.Id == quote.CheckoutBranchId)?.Name}\n{quote.FormattedAddress}\n{string.Join("\n", quote.Items.Select(x => $"{x.Quantity} × {x.Name}: {Money(x.Subtotal)}"))}\nSubtotal: {Money(quote.Subtotal)}\nDescuento: {Money(quote.DiscountTotal)}\nDomicilio: {Money(quote.EstimatedDeliveryFee)}\nTotal: {Money(quote.Total)}\nPago: {(paymentMethod == "online" ? "Wompi" : "Efectivo")}\nAl hacer el pedido aceptas el tratamiento de datos conforme a la política versión 2026-08-24.";

    private static bool AddOrReplace(List<WhatsAppCartItemState> cart, int productId, int quantity)
    {
        var item = cart.FirstOrDefault(x => x.ProductId == productId);
        if (item is null)
        {
            if (cart.Count >= 30) return false;
            cart.Add(new WhatsAppCartItemState { ProductId = productId, Quantity = quantity });
        }
        else item.Quantity = quantity;
        return true;
    }

    private static void ApplyCartCommand(WhatsAppCommerceState state, JsonElement data)
    {
        var productId = GetInt(data, "product_id");
        if (!productId.HasValue) return;
        var item = state.Cart.FirstOrDefault(x => x.ProductId == productId.Value);
        if (item is null) return;
        switch (GetString(data, "command"))
        {
            case "remove": state.Cart.Remove(item); break;
            case "increase": item.Quantity = Math.Min(50, item.Quantity + 1); break;
            case "decrease": if (--item.Quantity <= 0) state.Cart.Remove(item); break;
        }
    }

    private static Dictionary<string, object?> Complete(string flowToken, string message) => new()
    {
        ["screen"] = "SUCCESS",
        ["data"] = new Dictionary<string, object?>
        {
            ["extension_message_response"] = new Dictionary<string, object?>
            {
                ["params"] = new Dictionary<string, object?> { ["flow_token"] = flowToken, ["message"] = message }
            }
        }
    };

    private static Dictionary<string, object?> Error(string screen, string message, int version) => new()
    {
        ["screen"] = screen,
        ["data"] = new Dictionary<string, object?> { ["error_message"] = message, ["_session_version"] = version }
    };

    private void TrackEvent(
        WhatsAppCommerceSession session,
        string eventName,
        int? branchId,
        string? screen,
        string? discriminator = null)
    {
        db.WhatsAppCommerceEvents.Add(new WhatsAppCommerceEvent
        {
            TenantId = session.TenantId,
            Session = session,
            ConversationId = session.ConversationId,
            BranchId = branchId,
            EventKey = $"{session.CorrelationId:N}:{eventName}:{Guid.NewGuid():N}",
            EventName = eventName,
            Screen = screen,
            ReferenceId = eventName is "order_created" or "checkout_created" ? discriminator : null
        });
    }

    private static WhatsAppCommerceState DeserializeState(string json) =>
        JsonSerializer.Deserialize<WhatsAppCommerceState>(json, JsonOptions) ?? new WhatsAppCommerceState();
    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string Money(int value) => value.ToString("C0", CultureInfo.GetCultureInfo("es-CO"));
    private static string ShortTitle(string value) => value.Length <= 30 ? value : value[..29] + "…";
    private static string? GetString(JsonElement data, string name) => data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : null;
    private static int? GetInt(JsonElement data, string name)
    {
        return WhatsAppFlowPayload.Integer(data, name);
    }
    private static bool GetBool(JsonElement data, string name) => data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var value) && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed) && parsed);
}

public sealed class WhatsAppCommerceState
{
    public int SchemaVersion { get; set; } = 1;
    public string Name { get; set; } = string.Empty;
    public bool AmbiguousCustomer { get; set; }
    public string? FulfillmentType { get; set; }
    public int? SavedAddressId { get; set; }
    public int? SelectedBranchId { get; set; }
    public string? City { get; set; }
    public string? Address { get; set; }
    public string? FormattedAddress { get; set; }
    public string? AddressAdditionalInfo { get; set; }
    public string? AddressLabel { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public bool AddressRequiresConfirmation { get; set; }
    public string? Category { get; set; }
    public int ProductPage { get; set; }
    public List<WhatsAppCartItemState> Cart { get; set; } = [];
    public string? BenefitSelection { get; set; }
    public string? PaymentMethod { get; set; }
    public string? OrderNotes { get; set; }
    public string LastScreen { get; set; } = "FULFILLMENT";
}

public sealed class WhatsAppCartItemState
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
}

internal sealed record WhatsAppFlowProductOption(
    int ProductId,
    string Name,
    string VariantLabel,
    int Price,
    string AvailabilityStatus,
    string? PhotoUrl);
