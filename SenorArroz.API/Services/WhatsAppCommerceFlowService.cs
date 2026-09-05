using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SenorArroz.API.Controllers;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
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
    private static readonly HashSet<string> Screens =
    [
        "CATEGORY", "PRODUCT_GROUP", "PRODUCT_VARIANT", "CART", "FULFILLMENT", "ADDRESS_PICKUP",
        "BENEFITS", "PAYMENT", "SUMMARY", "RECOVERY"
    ];
    private readonly WhatsAppFlowOptions _options = options.Value;

    public static bool IsPurchaseIntent(string? text)
        => NormalizeCommand(text) is "pedido" or "pedir" or "comprar" or "hacer pedido" or "ver menu";

    public static bool IsGreeting(string? text)
        => NormalizeCommand(text) is "hola" or "buenas" or "buen dia" or "buenos dias" or "buenas tardes" or "buenas noches"
            or "hola buenas" or "hola buen dia" or "hola buenos dias" or "hola buenas tardes" or "hola buenas noches";

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
        var body = "No hay un checkout vigente para reintentar. Escribe PEDIDO para comenzar de nuevo o solicita un asesor.";
        if (checkout is not null && checkout.ExpiresAt > clock.UtcNow && checkout.OrderId is null && checkout.Status != "review_required")
        {
            try
            {
                payment = await wompi.RetryCheckoutAsync(1, checkout, clock.UtcNow, ct);
                body = "Puedes reintentar el pago dentro del plazo original de tu pedido. Te confirmaremos la aprobación por este chat.";
            }
            catch (BusinessException)
            {
                body = "No fue posible habilitar el reintento. Solicita un asesor para revisar el pago.";
            }
        }
        db.WhatsAppCommerceOutboxMessages.Add(new WhatsAppCommerceOutboxMessage
        {
            TenantId = 1,
            ChannelSettingId = channel.Id,
            ConversationId = conversationId,
            EventKey = eventKey,
            Body = body,
            Url = payment is null ? null : BuildWompiUrl(payment),
            ButtonText = payment is null ? null : "Reintentar pago",
            NextAttemptAt = clock.UtcNow
        });
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return true;
    }

    public async Task<bool> StartAsync(int conversationId, int channelSettingId, CancellationToken ct, bool greeting = false)
    {
        if (!_options.Enabled || _options.TenantId != 1) return false;
        var channel = await db.WhatsAppChannelSettings.AsNoTracking().FirstOrDefaultAsync(
            x => x.Id == channelSettingId && x.TenantId == 1 && x.IsActive && x.IsVerified && x.FlowEnabled, ct);
        if (channel is null || string.IsNullOrWhiteSpace(channel.FlowId)) return false;
        var conversation = await db.WhatsAppConversations.FirstOrDefaultAsync(
            x => x.Id == conversationId && x.TenantId == 1 && x.ChannelSettingId == channel.Id, ct);
        if (conversation is null) return false;
        var phone = ColombianMobilePhone.Normalize(conversation.PhoneNumber);
        if (!ColombianMobilePhone.IsValid(phone)
            || _options.RestrictToAllowlist && !_options.AllowedPhoneHashes.Contains(Sha256(phone), StringComparer.OrdinalIgnoreCase)) return false;
        var recipient = WhatsAppRecipientResolver.Resolve(conversation);
        if (recipient is null) return false;

        var session = await db.WhatsAppCommerceSessions.Include(x => x.Tokens)
            .Where(x => x.ConversationId == conversation.Id && x.Status == "active" && x.ExpiresAt > clock.UtcNow)
            .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        if (greeting && session is not null) return true;

        if (session is null)
        {
            var customerSession = await customerAuth.ResolveTrustedPhoneAsync(conversation.PhoneNumber ?? recipient, ct);
            var initialState = new WhatsAppCommerceState
            {
                Name = customerSession.Customer?.Name ?? conversation.ContactName ?? conversation.WhatsAppUsername ?? string.Empty,
                AmbiguousCustomer = customerSession.AmbiguousCustomer,
                LastScreen = "CATEGORY"
            };
            var initialToken = Base64Url(RandomNumberGenerator.GetBytes(32));
            var initialHash = Sha256(initialToken);
            var expiresAt = NextExpiration();
            session = new WhatsAppCommerceSession
            {
                TenantId = 1,
                ChannelSettingId = channel.Id,
                ConversationId = conversation.Id,
                CustomerId = customerSession.Customer?.Id,
                BranchId = conversation.OperationalBranchId,
                FlowTokenHash = initialHash,
                StateJson = JsonSerializer.Serialize(initialState, JsonOptions),
                IdempotencyKey = $"waf_{Guid.NewGuid():N}",
                ExpiresAt = expiresAt,
                Tokens = [new WhatsAppCommerceSessionToken { TenantId = 1, TokenHash = initialHash, ExpiresAt = expiresAt }]
            };
            db.WhatsAppCommerceSessions.Add(session);
            TrackEvent(session, "flow_started", session.BranchId, initialState.LastScreen, "v2");
            await db.SaveChangesAsync(ct);
            return await SendInvitationAsync(session, channel, conversation, recipient, initialToken, true, ct);
        }

        var rawToken = Base64Url(RandomNumberGenerator.GetBytes(32));
        var expiration = NextExpiration();
        session.ExpiresAt = expiration;
        foreach (var token in session.Tokens) token.ExpiresAt = expiration;
        var alias = new WhatsAppCommerceSessionToken
        {
            TenantId = 1,
            Session = session,
            TokenHash = Sha256(rawToken),
            ExpiresAt = expiration
        };
        session.Tokens.Add(alias);
        TrackEvent(session, "session_resumed", session.BranchId, DeserializeState(session.StateJson).LastScreen, "v2");
        await db.SaveChangesAsync(ct);
        var sent = await SendInvitationAsync(session, channel, conversation, recipient, rawToken, false, ct);
        if (!sent)
        {
            db.WhatsAppCommerceSessionTokens.Remove(alias);
            await db.SaveChangesAsync(ct);
        }
        return sent;
    }

    private async Task<bool> SendInvitationAsync(
        WhatsAppCommerceSession session,
        WhatsAppChannelSetting channel,
        WhatsAppConversation conversation,
        string recipient,
        string rawToken,
        bool isNew,
        CancellationToken ct)
    {
        var state = DeserializeState(session.StateJson);
        var sent = await cloud.SendFlowMessageAsync(
            channel.PhoneNumberId,
            channel.AccessToken,
            recipient,
            isNew
                ? "¡Hola! Te acompaño paso a paso para hacer tu pedido. Toca Ver menú para comenzar."
                : "Tu pedido sigue guardado. Toca Continuar pedido para seguir donde quedaste.",
            isNew ? "Ver menú" : "Continuar pedido",
            channel.FlowId!,
            rawToken,
            state.LastScreen,
            ct);
        if (!sent.Success)
        {
            if (isNew) session.Status = "abandoned";
            TrackEvent(session, "flow_send_failed", session.BranchId, state.LastScreen, "v2");
            await db.SaveChangesAsync(ct);
            logger.LogWarning("WhatsApp Flow send failed. ChannelId={ChannelId} ConversationId={ConversationId}", channel.Id, conversation.Id);
            return false;
        }
        conversation.LastMessageAt = clock.UtcNow;
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
            RawPayload = JsonSerializer.Serialize(new { origin = "whatsapp_flow_v2", sessionId = session.Id })
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
            .Include(x => x.Tokens)
            .FirstOrDefaultAsync(x => x.ChannelSettingId == channelId
                && (x.FlowTokenHash == tokenHash || x.Tokens.Any(token => token.TokenHash == tokenHash)), ct);
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
            TrackEvent(session, "flow_expired", session.BranchId, screen, "expired");
            await db.SaveChangesAsync(ct);
            return Recovery(session.Version, "Esta sesión venció. Cierra este menú y escribe PEDIDO para comenzar nuevamente.", false);
        }
        if (session.Status != "active")
            return Complete(flowToken, "Este pedido ya fue procesado. Revisa los mensajes del chat.");

        var state = DeserializeState(session.StateJson);
        if (action == "INIT") return await BuildScreenAsync(session, state, state.LastScreen, null, ct);
        if (action == "BACK")
        {
            var target = ResolveBackScreen(screen, state);
            state.LastScreen = target;
            session.Version++;
            TrackEvent(session, "back_navigation", session.BranchId, target, state.Category);
            return await BuildScreenAsync(session, state, target, null, ct);
        }
        if (action != "data_exchange") return await ShowRecoveryAsync(session, state, "No pudimos reconocer esa acción. Puedes reintentar.", true, ct);

        var requestedVersion = GetInt(data, "_session_version");
        if (requestedVersion != session.Version)
            return await BuildScreenAsync(session, state, state.LastScreen, "Actualizamos tu pedido. Revisa esta pantalla antes de continuar.", ct);
        var current = string.IsNullOrWhiteSpace(screen) ? state.LastScreen : screen.ToUpperInvariant();
        if (current != state.LastScreen)
            return await BuildScreenAsync(session, state, state.LastScreen, "Continúa desde la pantalla actual de tu pedido.", ct);

        var command = GetString(data, "command");
        if (command == "human") return await TransferToHumanAsync(session, current, flowToken, ct);

        string next = current;
        string? error = null;
        var catalog = current is "CATEGORY" or "PRODUCT_GROUP" or "PRODUCT_VARIANT" or "CART"
            ? await GetCatalogAsync(ct)
            : null;
        switch (current)
        {
            case "CATEGORY":
                state.Category = GetString(data, "category");
                if (state.Category is not ("rice" or "combo" or "beverage" or "addition"))
                    error = "Elige una categoría para continuar.";
                else
                {
                    state.SelectedProductGroup = null;
                    state.EditingProductId = null;
                    state.PendingRecommendationProductId = null;
                    next = "PRODUCT_GROUP";
                }
                break;
            case "PRODUCT_GROUP":
                if (command == "catalog")
                {
                    next = "CATEGORY";
                    break;
                }
                state.SelectedProductGroup = GetString(data, "product_group");
                if (GetGroups(catalog!, state.Category).All(x => x.Key != state.SelectedProductGroup))
                    error = "Elige una receta o producto disponible.";
                else next = "PRODUCT_VARIANT";
                break;
            case "PRODUCT_VARIANT":
                if (command == "groups")
                {
                    state.EditingProductId = null;
                    next = "PRODUCT_GROUP";
                    break;
                }
                if (command == "remove" && state.EditingProductId.HasValue)
                {
                    state.Cart.RemoveAll(x => x.ProductId == state.EditingProductId.Value);
                    InvalidateQuote(state);
                    state.EditingProductId = null;
                    next = state.Cart.Count == 0 ? "CATEGORY" : "CART";
                    break;
                }
                var group = GetGroups(catalog!, state.Category).FirstOrDefault(x => x.Key == state.SelectedProductGroup);
                var productId = GetInt(data, "product_id");
                var quantity = GetInt(data, "quantity");
                var selected = group?.Options.FirstOrDefault(x => x.ProductId == productId);
                if (selected is null || selected.AvailabilityStatus == "unavailable")
                    error = "Elige un tamaño disponible.";
                else if (quantity is null or < 1 or > 50)
                    error = "La cantidad debe estar entre 1 y 50.";
                else
                {
                    if (state.EditingProductId.HasValue && state.EditingProductId != selected.ProductId)
                        state.Cart.RemoveAll(x => x.ProductId == state.EditingProductId.Value);
                    if (!AddOrReplace(state.Cart, selected.ProductId, quantity.Value))
                        error = "El carrito admite máximo 30 productos distintos.";
                    else
                    {
                        if (state.PendingRecommendationProductId == selected.ProductId)
                            TrackEvent(session, "recommendation_added", session.BranchId, current, selected.ProductId.ToString(CultureInfo.InvariantCulture));
                        state.EditingProductId = null;
                        state.PendingRecommendationProductId = null;
                        InvalidateQuote(state);
                        next = "CART";
                    }
                }
                break;
            case "CART":
                if (state.Cart.Count == 0)
                {
                    error = "Tu carrito está vacío. Agrega un producto para continuar.";
                    next = "CATEGORY";
                    break;
                }
                if (command == "add")
                {
                    state.Category = null;
                    next = "CATEGORY";
                    break;
                }
                if (command == "edit")
                {
                    var editId = GetInt(data, "cart_product_id");
                    var match = FindProduct(catalog!, editId);
                    if (match is null || state.Cart.All(x => x.ProductId != editId))
                        error = "Selecciona el producto que quieres editar.";
                    else
                    {
                        state.Category = match.Value.Category;
                        state.SelectedProductGroup = match.Value.Group.Key;
                        state.EditingProductId = editId;
                        next = "PRODUCT_VARIANT";
                    }
                    break;
                }
                if (command == "recommendation")
                {
                    var recommendationId = GetInt(data, "recommendation_id");
                    var match = FindProduct(catalog!, recommendationId);
                    if (match is null) error = "Elige una sugerencia disponible.";
                    else
                    {
                        state.Category = match.Value.Category;
                        state.SelectedProductGroup = match.Value.Group.Key;
                        state.PendingRecommendationProductId = recommendationId;
                        next = "PRODUCT_VARIANT";
                    }
                    break;
                }
                if (command != "continue") error = "Elige cómo quieres continuar.";
                else if (!ContainsMainProduct(catalog!, state.Cart)) error = "Agrega al menos un arroz o combo para continuar.";
                else next = "FULFILLMENT";
                break;
            case "FULFILLMENT":
                state.FulfillmentType = GetString(data, "fulfillment_type");
                if (state.FulfillmentType is not ("delivery" or "pickup")) error = "Elige domicilio o recogida.";
                else
                {
                    ClearFulfillment(state);
                    state.AddressMode = state.FulfillmentType == "pickup" ? "pickup" : "saved";
                    InvalidateQuote(state);
                    next = "ADDRESS_PICKUP";
                }
                break;
            case "ADDRESS_PICKUP":
                (next, error) = await HandleAddressAsync(session, state, data, ct);
                break;
            case "BENEFITS":
                state.BenefitSelection = GetString(data, "benefit_selection");
                if (state.BenefitSelection == "none") state.BenefitSelection = null;
                InvalidateQuote(state);
                var benefitQuote = await GetQuoteAsync(session, state, ct);
                if (!benefitQuote.Success)
                    return await HandleQuoteFailureAsync(session, state, benefitQuote, ct);
                if (benefitQuote.Quote!.BenefitConflict)
                    error = "Elige solo un beneficio para continuar.";
                else next = "PAYMENT";
                break;
            case "PAYMENT":
                state.PaymentMethod = GetString(data, "payment_method");
                state.OrderNotes = GetString(data, "order_notes");
                if (state.PaymentMethod is not ("cash" or "online")) error = "Elige efectivo o pago en línea.";
                else
                {
                    var paymentQuote = await GetQuoteAsync(session, state, ct);
                    if (!paymentQuote.Success) return await HandleQuoteFailureAsync(session, state, paymentQuote, ct);
                    if (state.PaymentMethod == "online" && !paymentQuote.Quote!.OnlinePaymentAvailable)
                        error = "El pago en línea no está disponible ahora. Elige efectivo o solicita un asesor.";
                    else next = "SUMMARY";
                }
                break;
            case "SUMMARY":
                if (command == "cart") next = "CART";
                else if (command == "delivery") next = "FULFILLMENT";
                else if (command == "confirm") return await ConfirmAsync(session, state, flowToken, ct);
                else error = "Confirma el pedido o vuelve para modificarlo.";
                break;
            case "RECOVERY":
                if (command == "human") return await TransferToHumanAsync(session, current, flowToken, ct);
                if (command == "restart")
                {
                    var name = state.Name;
                    var ambiguous = state.AmbiguousCustomer;
                    state = new WhatsAppCommerceState { Name = name, AmbiguousCustomer = ambiguous, LastScreen = "CATEGORY" };
                    next = "CATEGORY";
                    TrackEvent(session, "flow_restarted", session.BranchId, next, "v2");
                }
                else
                {
                    next = Screens.Contains(state.RecoveryScreen ?? string.Empty) && state.RecoveryScreen != "RECOVERY"
                        ? state.RecoveryScreen!
                        : "CATEGORY";
                    TrackEvent(session, "flow_retried", session.BranchId, next, state.LastErrorCode);
                }
                state.RecoveryScreen = null;
                break;
            default:
                return await ShowRecoveryAsync(session, state, "Perdimos el paso actual, pero tu carrito sigue guardado.", true, ct);
        }

        state.LastScreen = next;
        session.BranchId = state.SelectedBranchId;
        session.Version++;
        return await BuildScreenAsync(session, state, next, error, ct);
    }

    private async Task<(string Next, string? Error)> HandleAddressAsync(
        WhatsAppCommerceSession session,
        WhatsAppCommerceState state,
        JsonElement data,
        CancellationToken ct)
    {
        state.Name = GetString(data, "name") ?? state.Name;
        if (state.Name.Length is < 2 or > 100) return ("ADDRESS_PICKUP", "Escribe el nombre de quien recibe.");
        var customer = await customerAuth.ResolveTrustedPhoneAsync(session.Conversation.PhoneNumber ?? string.Empty, ct);
        state.AmbiguousCustomer = customer.AmbiguousCustomer;
        session.CustomerId = customer.Customer?.Id;
        if (customer.AmbiguousCustomer)
            return ("ADDRESS_PICKUP", "Este número corresponde a varios clientes. Cierra el menú y escribe ASESOR.");

        if (state.FulfillmentType == "pickup")
        {
            state.SelectedBranchId = GetInt(data, "branch_id");
            state.SavedAddressId = null;
            if (!state.SelectedBranchId.HasValue || !await IsAvailableBranchAsync(state.SelectedBranchId.Value, ct))
                return ("ADDRESS_PICKUP", "Elige una sede disponible.");
            session.Conversation.OperationalBranchId = state.SelectedBranchId;
        }
        else
        {
            var savedValue = GetString(data, "saved_address_id");
            if (savedValue == "new" || GetString(data, "command") == "new_address")
            {
                state.AddressMode = "new";
                state.SavedAddressId = null;
                return ("ADDRESS_PICKUP", null);
            }
            if (state.AddressMode == "confirm" || state.AddressRequiresConfirmation)
            {
                var confirmationError = await ResolveNewAddressAsync(state, GetBool(data, "address_confirmed"), ct);
                if (confirmationError is not null) return ("ADDRESS_PICKUP", confirmationError);
            }
            else
            {
                state.SavedAddressId = GetInt(data, "saved_address_id");
                if (state.SavedAddressId.HasValue)
                {
                    if (customer.Addresses.All(x => x.Id != state.SavedAddressId.Value))
                        return ("ADDRESS_PICKUP", "Elige una dirección guardada válida.");
                    state.AddressMode = "saved";
                }
                else
                {
                    state.AddressMode = "new";
                    state.City = GetString(data, "city");
                    state.Address = GetString(data, "address");
                    state.AddressAdditionalInfo = GetString(data, "address_additional_info");
                    if (string.IsNullOrWhiteSpace(state.City) || string.IsNullOrWhiteSpace(state.Address))
                        return ("ADDRESS_PICKUP", "Completa ciudad y dirección.");
                    var addressError = await ResolveNewAddressAsync(state, false, ct);
                    if (addressError is not null) return ("ADDRESS_PICKUP", addressError);
                }
            }
        }

        InvalidateQuote(state);
        var quote = await GetQuoteAsync(session, state, ct);
        if (!quote.Success)
        {
            state.LastErrorCode = QuoteStatusName(quote.Status);
            TrackEvent(session, "quote_error", session.BranchId, "ADDRESS_PICKUP", state.LastErrorCode);
            if (quote.Status is WhatsAppQuoteStatus.CatalogChanged or WhatsAppQuoteStatus.TemporaryFailure)
            {
                state.RecoveryScreen = "ADDRESS_PICKUP";
                return ("RECOVERY", quote.Message);
            }
            return ("ADDRESS_PICKUP", quote.Message);
        }
        ApplyQuoteState(session, state, quote.Quote!);
        return (quote.Quote!.AvailableBenefits.Count > 0 ? "BENEFITS" : "PAYMENT", null);
    }

    private async Task<Dictionary<string, object?>> BuildScreenAsync(
        WhatsAppCommerceSession session,
        WhatsAppCommerceState state,
        string screen,
        string? error,
        CancellationToken ct)
    {
        TrackEvent(session, "screen_reached", session.BranchId, screen, $"v2:{state.Category ?? "none"}");
        if (!string.IsNullOrWhiteSpace(error))
            TrackEvent(session, "validation_error", session.BranchId, screen, "validation");
        var payload = new Dictionary<string, object?>
        {
            ["_session_version"] = session.Version,
            ["error_message"] = error ?? string.Empty,
            ["help_text"] = "¿Necesitas ayuda? Cierra este menú y escribe ASESOR.",
            ["name"] = state.Name,
            ["fulfillment_type"] = state.FulfillmentType ?? string.Empty,
            ["address_summary_text"] = string.Empty,
            ["cart_subtotal_text"] = string.Empty,
            ["order_summary_text"] = string.Empty
        };

        if (screen is "CATEGORY" or "PRODUCT_GROUP" or "PRODUCT_VARIANT" or "CART")
        {
            var catalog = await GetCatalogAsync(ct);
            payload["categories"] = CategoryOptions;
            payload["category_title"] = CategoryTitle(state.Category);
            if (screen == "PRODUCT_GROUP") await PopulateGroupsAsync(payload, catalog, state, ct);
            if (screen == "PRODUCT_VARIANT") PopulateVariants(payload, catalog, state);
            if (screen == "CART") PopulateCart(payload, catalog, state, session);
        }

        if (screen == "ADDRESS_PICKUP")
            await PopulateAddressAsync(payload, session, state, ct);

        if (screen is "BENEFITS" or "PAYMENT" or "SUMMARY")
        {
            var quoteResult = await GetQuoteAsync(session, state, ct);
            if (!quoteResult.Success)
                return await HandleQuoteFailureAsync(session, state, quoteResult, ct);
            var quote = quoteResult.Quote!;
            ApplyQuoteState(session, state, quote);
            payload["benefits"] = quote.AvailableBenefits
                .Select(x => new { id = x.Source, title = ShortTitle(x.Title), description = "Aplicar a este pedido" })
                .Prepend(new { id = "none", title = "Continuar sin beneficio", description = "No aplicar descuentos o premios" })
                .ToArray();
            payload["payment_methods"] = new[]
            {
                new { id = "cash", title = "Efectivo", description = "El pedido se confirma de inmediato", enabled = true },
                new { id = "online", title = "Pago en línea", description = "Reserva por 15 minutos y enlace Wompi", enabled = quote.OnlinePaymentAvailable }
            };
            payload["address_summary_text"] = BuildAddressSummary(quote);
            payload["cart_subtotal_text"] = $"Subtotal de productos: {Money(quote.Subtotal)}";
            payload["order_summary_text"] = BuildSummary(quote, state.PaymentMethod);
        }

        if (screen == "RECOVERY")
        {
            payload["recovery_options"] = RecoveryOptions(true);
            payload["error_message"] = error ?? "Tu pedido sigue guardado.";
        }

        state.LastScreen = screen;
        session.StateJson = JsonSerializer.Serialize(state, JsonOptions);
        await db.SaveChangesAsync(ct);
        return new() { ["screen"] = screen, ["data"] = payload };
    }

    private async Task PopulateGroupsAsync(
        Dictionary<string, object?> payload,
        PublicCatalogDto catalog,
        WhatsAppCommerceState state,
        CancellationToken ct)
    {
        var groups = GetGroups(catalog, state.Category).ToArray();
        var groupImages = images is null
            ? new string?[groups.Length]
            : await Task.WhenAll(groups.Select(group => string.IsNullOrWhiteSpace(group.PhotoUrl)
                ? Task.FromResult<string?>(null)
                : images.GetBase64Async(group.PhotoUrl, ct)));
        var rows = new List<Dictionary<string, object?>>(groups.Length);
        var imagePayloadSize = 0;
        for (var index = 0; index < groups.Length; index++)
        {
            var group = groups[index];
            var available = group.Options.Where(x => x.AvailabilityStatus != "unavailable").ToArray();
            var row = new Dictionary<string, object?>
            {
                ["id"] = group.Key,
                ["title"] = ShortTitle(group.Name),
                ["description"] = available.Length == 0 ? "Agotado" : $"Desde {Money(available.Min(x => x.Price))}",
                ["enabled"] = available.Length > 0
            };
            var image = groupImages[index];
            if (image is not null && imagePayloadSize + image.Length <= 700_000)
            {
                row["image"] = image;
                imagePayloadSize += image.Length;
            }
            rows.Add(row);
        }
        payload["product_groups"] = rows.Count > 0
            ? rows
            : [new Dictionary<string, object?> { ["id"] = "unavailable", ["title"] = "No hay productos disponibles", ["enabled"] = false }];
    }

    private static void PopulateVariants(Dictionary<string, object?> payload, PublicCatalogDto catalog, WhatsAppCommerceState state)
    {
        var group = GetGroups(catalog, state.Category).FirstOrDefault(x => x.Key == state.SelectedProductGroup);
        payload["product_group_name"] = group?.Name ?? "Producto";
        payload["product_group_description"] = group?.Description ?? group?.Ingredients ?? string.Empty;
        payload["product_variants"] = group?.Options.Select(x => new
        {
            id = x.ProductId.ToString(CultureInfo.InvariantCulture),
            title = ShortTitle(x.VariantLabel),
            description = $"{Money(x.Price)} · {PeopleText(x.ServesPeopleMin, x.ServesPeopleMax)}{(x.AvailabilityStatus == "lowStock" ? " · Pocas unidades" : string.Empty)}",
            enabled = x.AvailabilityStatus != "unavailable"
        }).ToArray() ?? [];
        var editing = state.EditingProductId.HasValue
            ? state.Cart.FirstOrDefault(x => x.ProductId == state.EditingProductId.Value)
            : null;
        payload["selected_product_id"] = editing?.ProductId.ToString(CultureInfo.InvariantCulture) ?? state.PendingRecommendationProductId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        payload["quantity"] = (editing?.Quantity ?? 1).ToString(CultureInfo.InvariantCulture);
        payload["editing_product"] = editing is not null;
        payload["variant_footer_label"] = editing is null ? "Agregar al pedido" : "Guardar cambios";
    }

    private void PopulateCart(
        Dictionary<string, object?> payload,
        PublicCatalogDto catalog,
        WhatsAppCommerceState state,
        WhatsAppCommerceSession session)
    {
        var products = AllProducts(catalog).ToDictionary(x => x.Option.ProductId);
        payload["cart_lines"] = state.Cart.Select(item =>
        {
            products.TryGetValue(item.ProductId, out var value);
            return new
            {
                id = item.ProductId.ToString(CultureInfo.InvariantCulture),
                title = ShortTitle($"{item.Quantity} × {value.Option?.Name ?? "Producto no disponible"}"),
                description = value.Option is null ? "Ya no está disponible" : $"{value.Option.VariantLabel} · {Money(value.Option.Price * item.Quantity)}"
            };
        }).ToArray();
        payload["cart_subtotal_text"] = $"Subtotal de productos: {Money(CalculateSubtotal(catalog, state.Cart))}";
        var recommendations = StorefrontRecommendationSelector.Select(catalog, state.Cart, 3).ToArray();
        payload["show_recommendations"] = recommendations.Length > 0;
        payload["recommendations"] = recommendations.Select(x => new
        {
            id = x.Option.ProductId.ToString(CultureInfo.InvariantCulture),
            title = ShortTitle(x.Group.Name),
            description = $"{x.Option.VariantLabel} · {Money(x.Option.Price)}"
        }).ToArray();
        foreach (var recommendation in recommendations)
            TrackEvent(session, "recommendation_shown", session.BranchId, "CART", recommendation.Option.ProductId.ToString(CultureInfo.InvariantCulture));
    }

    private async Task PopulateAddressAsync(
        Dictionary<string, object?> payload,
        WhatsAppCommerceSession session,
        WhatsAppCommerceState state,
        CancellationToken ct)
    {
        var customer = await customerAuth.ResolveTrustedPhoneAsync(session.Conversation.PhoneNumber ?? string.Empty, ct);
        state.AmbiguousCustomer = customer.AmbiguousCustomer;
        session.CustomerId = customer.Customer?.Id;
        if (customer.AmbiguousCustomer) payload["name"] = string.Empty;
        var addresses = customer.AmbiguousCustomer
            ? []
            : customer.Addresses.Select(x => new
            {
                id = x.Id.ToString(CultureInfo.InvariantCulture),
                title = ShortTitle(string.IsNullOrWhiteSpace(x.Label) ? "Dirección guardada" : x.Label),
                description = ShortDescription($"{x.Address}{(string.IsNullOrWhiteSpace(x.AdditionalInfo) ? string.Empty : $", {x.AdditionalInfo}")}")
            }).ToList();
        addresses.Add(new { id = "new", title = "Usar otra dirección", description = "Escribir una dirección diferente" });
        payload["saved_addresses"] = addresses;
        if (state.FulfillmentType == "delivery" && state.AddressMode == "saved" && customer.Addresses.Count == 0)
            state.AddressMode = "new";

        var availabilityAction = await storefront.GetBranchAvailability(ct);
        var availability = (availabilityAction.Result as OkObjectResult)?.Value as ApiResponse<IReadOnlyCollection<PublicBranchAvailabilityDto>>;
        var availableIds = availability?.Data?.Where(x => x.IsAvailable).Select(x => x.BranchId).ToArray() ?? [];
        var branches = await db.Branches.AsNoTracking().Where(x => availableIds.Contains(x.Id))
            .OrderBy(x => x.Name).Select(x => new { id = x.Id.ToString(), title = x.Name, description = x.Address }).ToListAsync(ct);
        payload["branches"] = branches.Count > 0
            ? branches
            : [new { id = "unavailable", title = "No hay sedes disponibles", description = "Cierra el menú y escribe ASESOR" }];
        payload["cities"] = new[] { new { id = "Medellín", title = "Medellín" }, new { id = "Bello", title = "Bello" }, new { id = "Copacabana", title = "Copacabana" } };
        payload["ambiguous_customer"] = customer.AmbiguousCustomer;
        payload["show_saved_addresses"] = state.FulfillmentType == "delivery" && state.AddressMode == "saved" && !customer.AmbiguousCustomer;
        payload["show_new_address"] = state.FulfillmentType == "delivery" && state.AddressMode == "new" && !state.AddressRequiresConfirmation && !customer.AmbiguousCustomer;
        payload["show_address_confirmation"] = state.FulfillmentType == "delivery" && state.AddressRequiresConfirmation && !customer.AmbiguousCustomer;
        payload["is_pickup"] = state.FulfillmentType == "pickup";
        payload["normalized_address"] = state.FormattedAddress ?? string.Empty;
        payload["address_summary_text"] = string.IsNullOrWhiteSpace(state.FormattedAddress) ? string.Empty : $"Dirección encontrada: {state.FormattedAddress}";
        payload["city"] = state.City ?? string.Empty;
        payload["address"] = state.Address ?? string.Empty;
        payload["address_additional_info"] = state.AddressAdditionalInfo ?? string.Empty;
    }

    private async Task<Dictionary<string, object?>> ConfirmAsync(
        WhatsAppCommerceSession session,
        WhatsAppCommerceState state,
        string flowToken,
        CancellationToken ct)
    {
        var customer = await customerAuth.ResolveTrustedPhoneAsync(session.Conversation.PhoneNumber ?? string.Empty, ct);
        if (customer.AmbiguousCustomer)
            return await BuildScreenAsync(session, state, "ADDRESS_PICKUP", "Este número corresponde a varios clientes. Cierra el menú y escribe ASESOR.", ct);
        InvalidateQuote(state);
        var quote = await GetQuoteAsync(session, state, ct);
        if (!quote.Success) return await HandleQuoteFailureAsync(session, state, quote, ct);
        var action = await storefront.ConfirmOrderTrusted(
            BuildOrderRequest(session, state),
            session.IdempotencyKey,
            customer,
            mapper,
            notifications,
            storefrontLogger,
            "whatsapp_flow",
            session.ConversationId,
            ct);
        if (action.Result is not OkObjectResult { Value: ApiResponse<PublicStorefrontOrderResult> response } || response.Data is null)
        {
            var message = action.Result is ObjectResult { Value: ApiResponse<PublicStorefrontOrderResult> rejected }
                ? rejected.Message
                : "No pudimos confirmar el pedido. Revisa el resumen o solicita un asesor.";
            state.LastErrorCode = "confirmation";
            TrackEvent(session, "confirmation_error", session.BranchId, "SUMMARY", state.LastErrorCode);
            return await ShowRecoveryAsync(session, state, message, true, ct);
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
        TrackEvent(session, result.OrderId.HasValue ? "order_created" : "checkout_created", result.BranchId, "SUCCESS", result.OrderId?.ToString(CultureInfo.InvariantCulture) ?? result.CheckoutId);
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

    private async Task<WhatsAppQuoteResult> GetQuoteAsync(WhatsAppCommerceSession session, WhatsAppCommerceState state, CancellationToken ct)
    {
        if (!CanQuote(state))
            return WhatsAppQuoteResult.Failure(WhatsAppQuoteStatus.Validation, "Completa el carrito y la entrega antes de continuar.");
        var fingerprint = QuoteFingerprint(state);
        if (state.LastQuoteFingerprint == fingerprint && !string.IsNullOrWhiteSpace(state.LastQuoteJson))
        {
            var cached = JsonSerializer.Deserialize<PublicDeliveryQuoteDto>(state.LastQuoteJson, JsonOptions);
            if (cached is not null) return WhatsAppQuoteResult.Ok(cached);
        }
        try
        {
            var customer = await customerAuth.ResolveTrustedPhoneAsync(session.Conversation.PhoneNumber ?? string.Empty, ct);
            var action = await storefront.QuoteTrusted(BuildOrderRequest(session, state), customer, ct);
            if (action.Result is OkObjectResult { Value: ApiResponse<PublicDeliveryQuoteDto> response } && response.Data is not null)
            {
                if (response.Data.IsOutsideCoverage)
                    return WhatsAppQuoteResult.Failure(WhatsAppQuoteStatus.OutsideCoverage, "Esta dirección está fuera de cobertura. Prueba otra dirección o elige recogida.");
                ApplyQuoteState(session, state, response.Data);
                state.LastQuoteFingerprint = QuoteFingerprint(state);
                state.LastQuoteJson = JsonSerializer.Serialize(response.Data, JsonOptions);
                return WhatsAppQuoteResult.Ok(response.Data);
            }
            if (action.Result is ObjectResult objectResult)
            {
                var message = objectResult.Value is ApiResponse<PublicDeliveryQuoteDto> rejected && !string.IsNullOrWhiteSpace(rejected.Message)
                    ? rejected.Message
                    : "No pudimos actualizar la cotización.";
                return WhatsAppQuoteResult.Failure(ClassifyQuoteFailure(objectResult.StatusCode, message), message);
            }
            return WhatsAppQuoteResult.Failure(WhatsAppQuoteStatus.TemporaryFailure, "No pudimos actualizar la cotización. Reintenta en unos segundos.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not DbUpdateException)
        {
            logger.LogWarning("WhatsApp Flow quote failed. SessionId={SessionId} ErrorType={ErrorType}", session.Id, ex.GetType().Name);
            return WhatsAppQuoteResult.Failure(WhatsAppQuoteStatus.TemporaryFailure, "No pudimos actualizar la cotización. Reintenta en unos segundos.");
        }
    }

    private async Task<Dictionary<string, object?>> HandleQuoteFailureAsync(
        WhatsAppCommerceSession session,
        WhatsAppCommerceState state,
        WhatsAppQuoteResult result,
        CancellationToken ct)
    {
        state.LastErrorCode = QuoteStatusName(result.Status);
        TrackEvent(session, "quote_error", session.BranchId, state.LastScreen, state.LastErrorCode);
        if (result.Status is WhatsAppQuoteStatus.OutsideCoverage or WhatsAppQuoteStatus.NoBranch or WhatsAppQuoteStatus.Validation)
        {
            state.LastScreen = result.Status == WhatsAppQuoteStatus.Validation ? "CART" : "ADDRESS_PICKUP";
            session.Version++;
            return await BuildScreenAsync(session, state, state.LastScreen, result.Message, ct);
        }
        return await ShowRecoveryAsync(session, state, result.Message, true, ct);
    }

    private async Task<Dictionary<string, object?>> ShowRecoveryAsync(
        WhatsAppCommerceSession session,
        WhatsAppCommerceState state,
        string message,
        bool recoverable,
        CancellationToken ct)
    {
        if (state.LastScreen != "RECOVERY") state.RecoveryScreen = state.LastScreen;
        state.LastScreen = "RECOVERY";
        session.Version++;
        TrackEvent(session, "recovery_shown", session.BranchId, "RECOVERY", state.LastErrorCode);
        var payload = Recovery(session.Version, message, recoverable);
        session.StateJson = JsonSerializer.Serialize(state, JsonOptions);
        await db.SaveChangesAsync(ct);
        return payload;
    }

    public static Dictionary<string, object?> Recovery(int version, string message, bool recoverable) => new()
    {
        ["screen"] = "RECOVERY",
        ["data"] = new Dictionary<string, object?>
        {
            ["_session_version"] = version,
            ["error_message"] = message,
            ["help_text"] = "También puedes cerrar este menú y escribir ASESOR.",
            ["recovery_options"] = RecoveryOptions(recoverable)
        }
    };

    public static Dictionary<string, object?> CompleteRecovery(string flowToken, string command) =>
        Complete(flowToken, command == "human"
            ? "Cierra este menú y escribe ASESOR para continuar."
            : "Cierra este menú y escribe PEDIDO para comenzar nuevamente.");

    private async Task<Dictionary<string, object?>> TransferToHumanAsync(
        WhatsAppCommerceSession session,
        string screen,
        string flowToken,
        CancellationToken ct)
    {
        session.Conversation.AttentionMode = WhatsAppAttentionMode.WaitingForHuman;
        session.Conversation.AttentionModeUpdatedAt = clock.UtcNow;
        session.Status = "completed";
        session.CompletedAt = clock.UtcNow;
        TrackEvent(session, "human_transfer", session.BranchId, screen, DeserializeState(session.StateJson).LastErrorCode);
        await db.SaveChangesAsync(ct);
        return Complete(flowToken, "Un asesor continuará contigo por este chat. Tu carrito quedó guardado.");
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
        BenefitSelection = state.BenefitSelection,
        PaymentMethod = state.PaymentMethod ?? "cash",
        OrderNotes = state.OrderNotes,
        Items = state.Cart.Select(x => new PublicCartItemRequest { ProductId = x.ProductId, Quantity = x.Quantity }).ToList()
    };

    private async Task<string?> ResolveNewAddressAsync(WhatsAppCommerceState state, bool confirmed, CancellationToken ct)
    {
        var action = await storefront.PreviewAddress(new PublicAddressPreviewRequest { City = state.City!, Address = state.Address! }, ct);
        if (action.Result is not OkObjectResult { Value: ApiResponse<PublicAddressPreviewDto> response } || response.Data is null)
            return action.Result is ObjectResult { Value: ApiResponse<PublicAddressPreviewDto> rejected }
                ? rejected.Message
                : "No pudimos ubicar la dirección. Revísala e intenta nuevamente.";
        var confirmedSameAddress = confirmed
            && state.AddressRequiresConfirmation
            && state.FormattedAddress == response.Data.FormattedAddress
            && state.Latitude == response.Data.Latitude
            && state.Longitude == response.Data.Longitude;
        state.FormattedAddress = response.Data.FormattedAddress;
        state.Latitude = response.Data.Latitude;
        state.Longitude = response.Data.Longitude;
        state.AddressRequiresConfirmation = response.Data.RequiresConfirmation && !confirmedSameAddress;
        state.AddressMode = state.AddressRequiresConfirmation ? "confirm" : "new";
        if (state.AddressRequiresConfirmation) return "Revisa y confirma la dirección encontrada.";
        state.Address = state.FormattedAddress;
        return null;
    }

    private async Task<PublicCatalogDto> GetCatalogAsync(CancellationToken ct)
    {
        var action = await storefront.GetCatalog(ct);
        return ((action.Result as OkObjectResult)?.Value as ApiResponse<PublicCatalogDto>)?.Data
            ?? throw new InvalidOperationException("El catálogo no está disponible.");
    }

    private async Task<bool> IsAvailableBranchAsync(int branchId, CancellationToken ct)
    {
        var action = await storefront.GetBranchAvailability(ct);
        return action.Result is OkObjectResult { Value: ApiResponse<IReadOnlyCollection<PublicBranchAvailabilityDto>> response }
            && response.Data?.Any(x => x.BranchId == branchId && x.IsAvailable) == true;
    }

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

    private static string BuildSummary(PublicDeliveryQuoteDto quote, string? paymentMethod)
    {
        var lines = string.Join("\n", quote.Items.Select(x => $"{x.Quantity} × {x.Name}: {Money(x.Subtotal)}"));
        var discount = quote.DiscountTotal > 0 ? $"\nDescuentos: -{Money(quote.DiscountTotal)}" : string.Empty;
        var delivery = quote.FulfillmentType == "delivery" ? $"\nDomicilio: {Money(quote.EstimatedDeliveryFee)}" : string.Empty;
        return $"{BuildAddressSummary(quote)}\n\n{lines}\nSubtotal: {Money(quote.Subtotal)}{discount}{delivery}\nTotal: {Money(quote.Total)}\nPago: {(paymentMethod == "online" ? "Wompi" : "Efectivo")}";
    }

    private static string BuildAddressSummary(PublicDeliveryQuoteDto quote)
    {
        var branch = quote.Branches.FirstOrDefault(x => x.Id == quote.CheckoutBranchId);
        return quote.FulfillmentType == "pickup"
            ? $"Recogida en {branch?.Name}\n{branch?.Address}"
            : $"Domicilio desde {branch?.Name}\n{quote.FormattedAddress}";
    }

    private static string ResolveBackScreen(string? requestedScreen, WhatsAppCommerceState state)
    {
        var requested = requestedScreen?.ToUpperInvariant();
        if (requested is not null && Screens.Contains(requested) && requested != state.LastScreen) return requested;
        return state.LastScreen switch
        {
            "PRODUCT_GROUP" => "CATEGORY",
            "PRODUCT_VARIANT" => "PRODUCT_GROUP",
            "CART" => "CATEGORY",
            "FULFILLMENT" => "CART",
            "ADDRESS_PICKUP" => "FULFILLMENT",
            "BENEFITS" => "ADDRESS_PICKUP",
            "PAYMENT" => "ADDRESS_PICKUP",
            "SUMMARY" => "PAYMENT",
            "RECOVERY" => state.RecoveryScreen ?? "CATEGORY",
            _ => "CATEGORY"
        };
    }

    private static IReadOnlyCollection<PublicProductGroupDto> GetGroups(PublicCatalogDto catalog, string? category) => category switch
    {
        "combo" => catalog.ComboGroups,
        "beverage" => catalog.BeverageGroups,
        "addition" => catalog.AdditionGroups,
        _ => catalog.RiceGroups
    };

    private static IEnumerable<(string Category, PublicProductGroupDto Group, PublicProductOptionDto Option)> AllProducts(PublicCatalogDto catalog) =>
        new[]
        {
            (Category: "rice", Groups: catalog.RiceGroups),
            (Category: "combo", Groups: catalog.ComboGroups),
            (Category: "beverage", Groups: catalog.BeverageGroups),
            (Category: "addition", Groups: catalog.AdditionGroups)
        }.SelectMany(entry => entry.Groups.SelectMany(group => group.Options.Select(option => (entry.Category, group, option))));

    private static (string Category, PublicProductGroupDto Group, PublicProductOptionDto Option)? FindProduct(PublicCatalogDto catalog, int? productId)
    {
        if (!productId.HasValue) return null;
        foreach (var product in AllProducts(catalog))
            if (product.Option.ProductId == productId.Value) return product;
        return null;
    }

    private static bool ContainsMainProduct(PublicCatalogDto catalog, IEnumerable<WhatsAppCartItemState> cart)
    {
        var ids = catalog.RiceGroups.Concat(catalog.ComboGroups).SelectMany(x => x.Options).Select(x => x.ProductId).ToHashSet();
        return cart.Any(x => ids.Contains(x.ProductId));
    }

    private static int CalculateSubtotal(PublicCatalogDto catalog, IEnumerable<WhatsAppCartItemState> cart)
    {
        var prices = AllProducts(catalog).ToDictionary(x => x.Option.ProductId, x => x.Option.Price);
        return cart.Sum(x => prices.TryGetValue(x.ProductId, out var price) ? price * x.Quantity : 0);
    }

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

    private static void ClearFulfillment(WhatsAppCommerceState state)
    {
        state.SavedAddressId = null;
        state.SelectedBranchId = null;
        state.City = null;
        state.Address = null;
        state.FormattedAddress = null;
        state.AddressAdditionalInfo = null;
        state.Latitude = null;
        state.Longitude = null;
        state.AddressRequiresConfirmation = false;
        state.BenefitSelection = null;
        state.PaymentMethod = null;
    }

    private static bool CanQuote(WhatsAppCommerceState state) => state.Cart.Count > 0
        && state.FulfillmentType is "delivery" or "pickup"
        && (state.FulfillmentType == "pickup" && state.SelectedBranchId.HasValue
            || state.FulfillmentType == "delivery" && (state.SavedAddressId.HasValue
                || !string.IsNullOrWhiteSpace(state.Address) && state.Latitude.HasValue && state.Longitude.HasValue && !state.AddressRequiresConfirmation));

    private static void ApplyQuoteState(WhatsAppCommerceSession session, WhatsAppCommerceState state, PublicDeliveryQuoteDto quote)
    {
        state.SelectedBranchId = quote.CheckoutBranchId;
        session.BranchId = quote.CheckoutBranchId;
        session.Conversation.OperationalBranchId = quote.CheckoutBranchId;
    }

    private static void InvalidateQuote(WhatsAppCommerceState state)
    {
        state.LastQuoteFingerprint = null;
        state.LastQuoteJson = null;
    }

    private static string QuoteFingerprint(WhatsAppCommerceState state)
    {
        var json = JsonSerializer.Serialize(new
        {
            state.FulfillmentType,
            state.SavedAddressId,
            state.SelectedBranchId,
            state.City,
            state.Address,
            state.AddressAdditionalInfo,
            state.Latitude,
            state.Longitude,
            state.BenefitSelection,
            cart = state.Cart.OrderBy(x => x.ProductId).Select(x => new { x.ProductId, x.Quantity })
        }, JsonOptions);
        return Sha256(json);
    }

    private static WhatsAppQuoteStatus ClassifyQuoteFailure(int? statusCode, string message)
    {
        if (message.Contains("cobertura", StringComparison.OrdinalIgnoreCase)) return WhatsAppQuoteStatus.OutsideCoverage;
        if (message.Contains("sede", StringComparison.OrdinalIgnoreCase)
            && message.Contains("dispon", StringComparison.OrdinalIgnoreCase)) return WhatsAppQuoteStatus.NoBranch;
        return statusCode switch
        {
            409 => WhatsAppQuoteStatus.CatalogChanged,
            >= 500 => WhatsAppQuoteStatus.TemporaryFailure,
            _ => WhatsAppQuoteStatus.Validation
        };
    }

    private static string QuoteStatusName(WhatsAppQuoteStatus status) => status switch
    {
        WhatsAppQuoteStatus.OutsideCoverage => "outside_coverage",
        WhatsAppQuoteStatus.NoBranch => "no_branch",
        WhatsAppQuoteStatus.CatalogChanged => "catalog_changed",
        WhatsAppQuoteStatus.TemporaryFailure => "temporary_failure",
        _ => "validation"
    };

    private static object[] RecoveryOptions(bool recoverable) => recoverable
        ?
        [
            new { id = "retry", title = "Reintentar", description = "Volver al último paso guardado" },
            new { id = "restart", title = "Comenzar nuevamente", description = "Vaciar este carrito y volver al menú" },
            new { id = "human", title = "Hablar con un asesor", description = "Conservar el contexto para recibir ayuda" }
        ]
        :
        [
            new { id = "restart", title = "Comenzar nuevamente", description = "Cerrar y escribir PEDIDO en el chat" },
            new { id = "human", title = "Hablar con un asesor", description = "Cerrar y escribir ASESOR en el chat" }
        ];

    private static object[] CategoryOptions =>
    [
        new { id = "rice", title = "Arroces", description = "Nuestras recetas en cinco tamaños" },
        new { id = "combo", title = "Combos", description = "Opciones listas para compartir" },
        new { id = "beverage", title = "Bebidas", description = "Para acompañar tu pedido" },
        new { id = "addition", title = "Adiciones", description = "Complementos y extras" }
    ];

    private static string CategoryTitle(string? category) => category switch
    {
        "rice" => "Arroces",
        "combo" => "Combos",
        "beverage" => "Bebidas",
        "addition" => "Adiciones",
        _ => "Menú"
    };

    private static string PeopleText(int? min, int? max) => (min, max) switch
    {
        (null, null) => "1 unidad",
        (var from, var to) when from == to => $"Para {from} persona{(from == 1 ? string.Empty : "s")}",
        _ => $"Para {min ?? 1} a {max ?? min ?? 1} personas"
    };

    private static string NormalizeCommand(string? text)
    {
        var withoutDiacritics = new string((text ?? string.Empty).Trim().ToLowerInvariant()
            .Normalize(NormalizationForm.FormD)
            .Where(x => CharUnicodeInfo.GetUnicodeCategory(x) != UnicodeCategory.NonSpacingMark)
            .ToArray()).Normalize(NormalizationForm.FormC);
        var words = new string(withoutDiacritics.Select(x => char.IsPunctuation(x) ? ' ' : x).ToArray());
        return string.Join(' ', words.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private void TrackEvent(WhatsAppCommerceSession session, string eventName, int? branchId, string? screen, string? discriminator = null)
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
            ReferenceId = string.IsNullOrWhiteSpace(discriminator) ? null : discriminator.Length <= 100 ? discriminator : discriminator[..100]
        });
    }

    private DateTime NextExpiration() => clock.UtcNow.AddMinutes(Math.Clamp(_options.SessionLifetimeMinutes, 15, 120));
    private static WhatsAppCommerceState DeserializeState(string json) =>
        JsonSerializer.Deserialize<WhatsAppCommerceState>(json, JsonOptions) ?? new WhatsAppCommerceState();
    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string Money(int value) => value.ToString("C0", CultureInfo.GetCultureInfo("es-CO"));
    private static string ShortTitle(string value) => value.Length <= 30 ? value : value[..29] + "…";
    private static string ShortDescription(string value) => value.Length <= 300 ? value : value[..299] + "…";
    private static string? GetString(JsonElement data, string name) => data.ValueKind == JsonValueKind.Object
        && data.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;
    private static int? GetInt(JsonElement data, string name) => WhatsAppFlowPayload.Integer(data, name);
    private static bool GetBool(JsonElement data, string name) => data.ValueKind == JsonValueKind.Object
        && data.TryGetProperty(name, out var value)
        && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed) && parsed);

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
}

public sealed class WhatsAppCommerceState
{
    public int SchemaVersion { get; set; } = 2;
    public string Name { get; set; } = string.Empty;
    public bool AmbiguousCustomer { get; set; }
    public string? FulfillmentType { get; set; }
    public int? SavedAddressId { get; set; }
    public int? SelectedBranchId { get; set; }
    public string? City { get; set; }
    public string? Address { get; set; }
    public string? FormattedAddress { get; set; }
    public string? AddressAdditionalInfo { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public bool AddressRequiresConfirmation { get; set; }
    public string AddressMode { get; set; } = "saved";
    public string? Category { get; set; }
    public string? SelectedProductGroup { get; set; }
    public int? EditingProductId { get; set; }
    public int? PendingRecommendationProductId { get; set; }
    public List<WhatsAppCartItemState> Cart { get; set; } = [];
    public string? BenefitSelection { get; set; }
    public string? PaymentMethod { get; set; }
    public string? OrderNotes { get; set; }
    public string? LastQuoteFingerprint { get; set; }
    public string? LastQuoteJson { get; set; }
    public string? LastErrorCode { get; set; }
    public string? RecoveryScreen { get; set; }
    public string LastScreen { get; set; } = "CATEGORY";
}

public sealed class WhatsAppCartItemState
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
}

internal enum WhatsAppQuoteStatus
{
    Success,
    Validation,
    OutsideCoverage,
    NoBranch,
    CatalogChanged,
    TemporaryFailure
}

internal sealed record WhatsAppQuoteResult(WhatsAppQuoteStatus Status, PublicDeliveryQuoteDto? Quote, string Message)
{
    public bool Success => Status == WhatsAppQuoteStatus.Success && Quote is not null;
    public static WhatsAppQuoteResult Ok(PublicDeliveryQuoteDto quote) => new(WhatsAppQuoteStatus.Success, quote, string.Empty);
    public static WhatsAppQuoteResult Failure(WhatsAppQuoteStatus status, string message) => new(status, null, message);
}
