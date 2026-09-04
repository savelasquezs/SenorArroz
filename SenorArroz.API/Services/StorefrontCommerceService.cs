using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SenorArroz.API.Security;
using SenorArroz.API.Services;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Services;
using SenorArroz.Shared.Models;

using SenorArroz.API.Controllers;

namespace SenorArroz.API.Services;

public sealed class StorefrontCommerceService(
    IApplicationDbContext db,
    IGoogleRoutesDrivingMetricsService routes,
    GoogleAddressGeocoder geocoder,
    IClock clock,
    IBranchBusinessHoursService businessHours,
    IMemoryCache cache,
    StorefrontQuoteConcurrencyGate concurrencyGate,
    IWompiPaymentService wompi,
    IOptions<StorefrontCustomerAuthOptions> storefrontOptions,
    IBackgroundWorkSignal<PaymentNotificationOutboxWork>? paymentNotificationSignal = null)
{
    private const int PreparationMinutes = 20;
    private const int DeliveryPromiseMinMinutes = 35;
    private const int DeliveryPromiseMaxMinutes = 45;
    private const int CoverageTravelMinutes = 30;
    private const int CoverageDistanceMeters = 5_000;
    private const int DeliveryBaseDistanceMeters = 2_000;
    private const int DeliveryBaseFee = 3_000;
    private const int DeliveryFeePerAdditionalKilometer = 1_000;
    private const int MaxWhatsAppMessageLength = 3500;
    private static readonly HashSet<string> PublicRoles = ["rice", "combo", "beverage", "addition"];
    private static readonly HashSet<string> MainRoles = ["rice", "combo"];
    private static readonly TimeSpan MapCacheDuration = TimeSpan.FromMinutes(5);
    private static readonly string[] AllowedCities = ["Medellín", "Bello", "Copacabana"];

    private static OkObjectResult Ok(object value) => new(value);
    private static BadRequestObjectResult BadRequest(object value) => new(value);
    private static UnauthorizedObjectResult Unauthorized(object value) => new(value);
    private static ConflictObjectResult Conflict(object value) => new(value);
    private static ObjectResult StatusCode(int code, object value) => new(value) { StatusCode = code };

    public async Task<ActionResult<ApiResponse<PublicCatalogDto>>> GetCatalog(CancellationToken cancellationToken)
    {
        var products = await db.Products
            .AsNoTracking()
            .Include(x => x.Category)
            .Include(x => x.CommercialProfile)
            .Where(x => x.Active && PublicRoles.Contains(x.Category.StorefrontRole))
            .OrderBy(x => x.Category.Name)
            .ThenBy(x => x.StorefrontSortOrder)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var branchRows = await GetEligibleBranches(cancellationToken);
        var branchIds = branchRows.Select(x => x.Id).ToList();
        var hours = await businessHours.GetBusinessHoursMany(branchIds, cancellationToken);
        var branches = branchRows
            .OrderBy(x => x.Name)
            .Select(x => new PublicBranchDto(
                x.Id,
                x.Name,
                x.Address,
                x.Latitude,
                x.Longitude,
                BuildWhatsAppUrl(x.ContactPhone),
                hours[x.Id]
                    .Select(hour => new PublicBranchBusinessHourDto(
                        hour.DayOfWeek, hour.OpenTime, hour.CloseTime, hour.IsClosed, hour.DisplayOrder))
                    .ToList()))
            .ToList();

        var result = new PublicCatalogDto(
            BuildGroups(products, "rice"),
            BuildGroups(products, "combo"),
            BuildGroups(products, "beverage"),
            BuildGroups(products, "addition"),
            [],
            branches,
            AllowedCities,
            PreparationMinutes,
            CoverageTravelMinutes,
            CoverageDistanceMeters);

        return Ok(ApiResponse<PublicCatalogDto>.SuccessResponse(result));
    }

    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<PublicBranchAvailabilityDto>>>> GetBranchAvailability(
        CancellationToken cancellationToken)
    {
        var branches = await EligibleBranchesQuery()
            .Select(branch => new
            {
                branch.Id,
                CanReceiveOrders = branch.StorefrontTakenByUserId.HasValue
                    && db.Users.Any(user => user.Id == branch.StorefrontTakenByUserId
                        && user.BranchId == branch.Id
                        && user.Active),
            })
            .ToListAsync(cancellationToken);
        var evaluations = await businessHours.EvaluateMany(branches.Select(branch => branch.Id), clock.UtcNow, cancellationToken);
        IReadOnlyCollection<PublicBranchAvailabilityDto> result = branches
            .Select(branch =>
            {
                var evaluation = evaluations[branch.Id];
                var isClosingSoon = evaluation.CurrentClosingAtUtc.HasValue
                    && evaluation.CurrentClosingAtUtc.Value > clock.UtcNow
                    && evaluation.CurrentClosingAtUtc.Value <= clock.UtcNow.AddHours(1);
                return new PublicBranchAvailabilityDto(
                    branch.Id,
                    evaluation.IsConfigured,
                    evaluation.IsOpen,
                    branch.CanReceiveOrders,
                    evaluation.IsConfigured && evaluation.IsOpen && branch.CanReceiveOrders,
                    evaluation.NextOpeningAtUtc,
                    evaluation.CurrentClosingAtUtc,
                    isClosingSoon);
            })
            .ToList();

        return Ok(ApiResponse<IReadOnlyCollection<PublicBranchAvailabilityDto>>.SuccessResponse(result));
    }

    public async Task<ActionResult<ApiResponse<PublicAddressPreviewDto>>> PreviewAddress(
        PublicAddressPreviewRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedCity = MatchAllowedCity(request.City);
        if (normalizedCity is null)
            return BadRequest(ApiResponse<PublicAddressPreviewDto>.ErrorResponse("La ciudad debe ser Medellín, Bello o Copacabana."));

        var resolved = await ResolveAddressCached(request.Address, null, null, cancellationToken);
        if (resolved.Result is null)
            return BadRequest(ApiResponse<PublicAddressPreviewDto>.ErrorResponse(resolved.Error ?? "No fue posible ubicar la dirección."));

        if (!AddressMatchesCity(resolved.Result.FormattedAddress, normalizedCity))
            return BadRequest(ApiResponse<PublicAddressPreviewDto>.ErrorResponse($"La ubicación encontrada no pertenece a {normalizedCity}."));

        var result = new PublicAddressPreviewDto(
            resolved.Result.FormattedAddress,
            resolved.Result.Latitude,
            resolved.Result.Longitude,
            resolved.Result.RequiresConfirmation);
        return Ok(ApiResponse<PublicAddressPreviewDto>.SuccessResponse(result));
    }

    public async Task<ActionResult<ApiResponse<PublicCoveragePreviewDto>>> PreviewCoverage(
        PublicCoveragePreviewRequest request,
        CancellationToken cancellationToken)
    {
        using var concurrencyLease = await concurrencyGate.TryEnter(cancellationToken);
        if (concurrencyLease is null)
            return StatusCode(StatusCodes.Status429TooManyRequests,
                ApiResponse<PublicCoveragePreviewDto>.ErrorResponse("Hay muchas validaciones en curso. Intenta nuevamente en unos segundos."));

        var normalizedCity = MatchAllowedCity(request.City);
        if (normalizedCity is null)
            return BadRequest(ApiResponse<PublicCoveragePreviewDto>.ErrorResponse("La ciudad debe ser Medellín, Bello o Copacabana."));

        var resolved = await ResolveAddressCached(request.Address, request.Latitude, request.Longitude, cancellationToken);
        if (resolved.Result is null)
            return BadRequest(ApiResponse<PublicCoveragePreviewDto>.ErrorResponse(resolved.Error ?? "No fue posible validar la ubicación."));

        if (!request.Latitude.HasValue || !request.Longitude.HasValue)
            return BadRequest(ApiResponse<PublicCoveragePreviewDto>.ErrorResponse("Selecciona y confirma una dirección exacta en Google Maps."));

        if (!AddressMatchesCity(resolved.Result.FormattedAddress, normalizedCity))
            return BadRequest(ApiResponse<PublicCoveragePreviewDto>.ErrorResponse($"La ubicación confirmada no pertenece a {normalizedCity}."));

        var branchRows = await GetEligibleBranches(cancellationToken);
        if (branchRows.Count == 0)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiResponse<PublicCoveragePreviewDto>.ErrorResponse("No hay sucursales habilitadas para pedidos web."));

        var routeResults = await GetValidRoutes(
            branchRows,
            ((double)resolved.Result.Latitude, (double)resolved.Result.Longitude),
            cancellationToken);
        if (routeResults.Count == 0)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiResponse<PublicCoveragePreviewDto>.ErrorResponse("Google Maps no pudo calcular el desplazamiento en este momento."));

        var nearest = routeResults[0];
        var result = new PublicCoveragePreviewDto(
            resolved.Result.FormattedAddress,
            resolved.Result.Latitude,
            resolved.Result.Longitude,
            routeResults.Select(x => ToBranchQuote(x, nearest.Branch.Id, nearest.Branch.Id)).ToList(),
            nearest.Branch.Id,
            CoverageDistanceMeters,
            CoverageTravelMinutes);
        return Ok(ApiResponse<PublicCoveragePreviewDto>.SuccessResponse(result));
    }

    public Task<ActionResult<ApiResponse<PublicDeliveryQuoteDto>>> QuoteTrusted(
        PublicDeliveryQuoteRequest request,
        StorefrontCustomerSessionResult customerSession,
        CancellationToken cancellationToken)
        => QuoteCore(request, cancellationToken, null, customerSession);

    public async Task<ActionResult<ApiResponse<PublicDeliveryQuoteDto>>> QuoteCore(
        PublicDeliveryQuoteRequest request,
        CancellationToken cancellationToken,
        StorefrontCustomerAuthService? customerAuth,
        StorefrontCustomerSessionResult? trustedSession,
        string? sessionToken = null)
    {
        using var concurrencyLease = await concurrencyGate.TryEnter(cancellationToken);
        if (concurrencyLease is null)
            return StatusCode(StatusCodes.Status429TooManyRequests,
                ApiResponse<PublicDeliveryQuoteDto>.ErrorResponse("Hay muchas cotizaciones en curso. Intenta nuevamente en unos segundos."));

        var fulfillmentType = Normalize(request.FulfillmentType);
        if (fulfillmentType is not ("delivery" or "pickup"))
            return BadRequest(ApiResponse<PublicDeliveryQuoteDto>.ErrorResponse("Selecciona si deseas domicilio o recoger en el local."));

        request.Phone = ColombianMobilePhone.Normalize(request.Phone);
        if (!ColombianMobilePhone.IsValid(request.Phone))
            return BadRequest(ApiResponse<PublicDeliveryQuoteDto>.ErrorResponse("Ingresa un celular colombiano válido de 10 dígitos."));
        if (!Validator.TryValidateObject(request, new ValidationContext(request), null, true)
            || request.Items is null || request.Items.Any(item => item is null
                || !Validator.TryValidateObject(item, new ValidationContext(item), null, true)))
            return BadRequest(ApiResponse<PublicDeliveryQuoteDto>.ErrorResponse("Revisa el nombre, celular, dirección y cantidades del pedido."));

        StorefrontCustomerSessionResult? verifiedSession = trustedSession;
        if (verifiedSession is null && customerAuth is not null && !string.IsNullOrWhiteSpace(sessionToken))
        {
            try
            {
                var session = await customerAuth.GetSessionAsync(sessionToken, cancellationToken);
                if (session.Phone == request.Phone && !session.AmbiguousCustomer)
                    verifiedSession = session;
            }
            catch (StorefrontAuthInvalidSessionException)
            {
                verifiedSession = null;
            }
        }

        Address? savedAddress = null;
        if (request.SavedAddressId.HasValue)
        {
            if (verifiedSession?.Customer is null)
                return Unauthorized(ApiResponse<PublicDeliveryQuoteDto>.ErrorResponse("Verifica tu celular para usar una dirección guardada."));
            savedAddress = await db.Addresses.AsNoTracking().FirstOrDefaultAsync(
                x => x.Id == request.SavedAddressId && x.CustomerId == verifiedSession.Customer.Id, cancellationToken);
            if (savedAddress is null)
                return BadRequest(ApiResponse<PublicDeliveryQuoteDto>.ErrorResponse("La dirección guardada ya no está disponible."));
            request.Address = savedAddress.AddressText;
            request.AddressAdditionalInfo = savedAddress.AdditionalInfo;
            request.Latitude = savedAddress.Latitude;
            request.Longitude = savedAddress.Longitude;
        }

        var branchRows = await GetEligibleBranches(cancellationToken);
        if (branchRows.Count == 0)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiResponse<PublicDeliveryQuoteDto>.ErrorResponse("No hay sucursales habilitadas para pedidos web."));

        var hoursEvaluation = await businessHours.EvaluateMany(branchRows.Select(x => x.Id), clock.UtcNow, cancellationToken);
        if (fulfillmentType == "pickup")
        {
            var requestedBranch = branchRows.FirstOrDefault(x => x.Id == request.SelectedBranchId);
            if (requestedBranch is null)
                return BadRequest(ApiResponse<PublicDeliveryQuoteDto>.ErrorResponse("Selecciona una sucursal habilitada para recoger tu pedido."));
            var evaluation = hoursEvaluation[requestedBranch.Id];
            if (!evaluation.IsConfigured)
                return Conflict(ApiResponse<PublicDeliveryQuoteDto>.ErrorResponse($"{requestedBranch.Name} no tiene un horario de atención válido y no puede recibir pedidos web."));
            if (!evaluation.IsOpen)
                return Conflict(ApiResponse<PublicDeliveryQuoteDto>.ErrorResponse(ClosedBranchMessage(requestedBranch.Name, evaluation.NextOpeningAtUtc)));
        }
        else
        {
            var openBranches = branchRows.Where(x => hoursEvaluation[x.Id].IsConfigured && hoursEvaluation[x.Id].IsOpen).ToList();
            if (openBranches.Count == 0)
            {
                var nextOpening = hoursEvaluation.Values
                    .Where(x => x.IsConfigured && x.NextOpeningAtUtc.HasValue)
                    .Select(x => x.NextOpeningAtUtc!.Value)
                    .DefaultIfEmpty()
                    .Min();
                var closedMessage = nextOpening == default
                    ? "En este momento no hay sedes con horario válido disponibles para recibir pedidos web."
                    : $"En este momento todas nuestras sedes están cerradas. Volvemos a atender {FormatOpening(nextOpening)}.";
                return Conflict(ApiResponse<PublicDeliveryQuoteDto>.ErrorResponse(closedMessage));
            }
            branchRows = openBranches;
        }

        GeocodedAddress? resolvedAddress = null;
        string? normalizedCity = null;
        if (fulfillmentType == "delivery")
        {
            var resolved = await ResolveAddressCached(request.Address ?? string.Empty, request.Latitude, request.Longitude, cancellationToken);
            if (resolved.Result is null)
                return BadRequest(ApiResponse<PublicDeliveryQuoteDto>.ErrorResponse(resolved.Error ?? "No fue posible validar la ubicación."));

            if (savedAddress is null && (!request.Latitude.HasValue || !request.Longitude.HasValue))
                return BadRequest(ApiResponse<PublicDeliveryQuoteDto>.ErrorResponse("Selecciona y confirma una dirección exacta en Google Maps."));

            normalizedCity = MatchAllowedCity(request.City)
                ?? AllowedCities.FirstOrDefault(x => AddressMatchesCity(resolved.Result.FormattedAddress, x));
            if (normalizedCity is null)
                return BadRequest(ApiResponse<PublicDeliveryQuoteDto>.ErrorResponse("La dirección debe pertenecer a Medellín, Bello o Copacabana."));
            if (!AddressMatchesCity(resolved.Result.FormattedAddress, normalizedCity))
                return BadRequest(ApiResponse<PublicDeliveryQuoteDto>.ErrorResponse($"La ubicación confirmada no pertenece a {normalizedCity}."));
            resolvedAddress = resolved.Result;
        }
        else if (!request.SelectedBranchId.HasValue || branchRows.All(x => x.Id != request.SelectedBranchId.Value))
        {
            return BadRequest(ApiResponse<PublicDeliveryQuoteDto>.ErrorResponse("Selecciona una sucursal habilitada para recoger tu pedido."));
        }

        var productIds = request.Items.Select(x => x.ProductId).Distinct().ToList();
        var products = await db.Products
            .AsNoTracking()
            .Include(x => x.Category)
            .Where(x => productIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var validationError = ValidateItems(request.Items, products);
        if (validationError is not null)
            return BadRequest(ApiResponse<PublicDeliveryQuoteDto>.ErrorResponse(validationError));

        var cartLines = request.Items.Select(item =>
        {
            var product = products[item.ProductId];
            return new PublicCartLineDto(product.Id, product.Name, item.Quantity, product.Price, product.Price * item.Quantity, item.Notes?.Trim());
        }).ToList();
        var subtotal = cartLines.Sum(x => x.Subtotal);

        IReadOnlyCollection<PublicBranchQuoteDto> branchOptions;
        BranchRouteSource checkoutBranch;
        int recommendedBranchId;
        int selectedBranchId;
        int travelMinutes;
        int distanceMeters;
        int estimatedDeliveryFee;
        int checkoutTravelMinutes;
        int checkoutDeliveryFee;
        bool outsideCoverage;

        if (fulfillmentType == "delivery")
        {
            var routeResults = await GetValidRoutes(
                branchRows,
                ((double)resolvedAddress!.Latitude, (double)resolvedAddress.Longitude),
                cancellationToken);
            if (routeResults.Count == 0)
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    ApiResponse<PublicDeliveryQuoteDto>.ErrorResponse("Google Maps no pudo calcular el desplazamiento en este momento."));

            var nearest = routeResults[0];
            var selected = routeResults.FirstOrDefault(x => x.Branch.Id == request.SelectedBranchId) ?? nearest;
            selectedBranchId = selected.Branch.Id;
            recommendedBranchId = nearest.Branch.Id;
            travelMinutes = ToMinutes(selected.Metrics.DurationSeconds);
            distanceMeters = selected.Metrics.DistanceMeters;
            estimatedDeliveryFee = EstimateDeliveryFee(distanceMeters);
            outsideCoverage = !IsWithinCoverage(selected.Metrics);
            var checkout = outsideCoverage ? nearest : selected;
            checkoutBranch = checkout.Branch;
            checkoutTravelMinutes = ToMinutes(checkout.Metrics.DurationSeconds);
            checkoutDeliveryFee = EstimateDeliveryFee(checkout.Metrics.DistanceMeters);
            branchOptions = routeResults.Select(x => ToBranchQuote(x, nearest.Branch.Id, selected.Branch.Id)).ToList();
            if (savedAddress is not null)
            {
                estimatedDeliveryFee = savedAddress.DeliveryFee;
                checkoutDeliveryFee = savedAddress.DeliveryFee;
                branchOptions = branchOptions.Select(x => x with { EstimatedDeliveryFee = savedAddress.DeliveryFee }).ToList();
            }
        }
        else
        {
            checkoutBranch = branchRows.Single(x => x.Id == request.SelectedBranchId!.Value);
            recommendedBranchId = checkoutBranch.Id;
            selectedBranchId = checkoutBranch.Id;
            travelMinutes = 0;
            distanceMeters = 0;
            estimatedDeliveryFee = 0;
            checkoutTravelMinutes = 0;
            checkoutDeliveryFee = 0;
            outsideCoverage = false;
            branchOptions = branchRows
                .OrderBy(x => x.Name)
                .Select(x => new PublicBranchQuoteDto(
                    x.Id, x.Name, x.Address, x.Latitude, x.Longitude, 0, 0, PreparationMinutes, 0, true,
                    x.Id == checkoutBranch.Id, x.Id == checkoutBranch.Id, null))
                .ToList();
        }

        var promotion = await GetActivePromotion(checkoutBranch.Id, cancellationToken);
        if (promotion?.MinimumOrderValue is int minimumOrderValue && subtotal < minimumOrderValue)
            promotion = null;
        var promotionDto = promotion is null ? null : ToPromotionDto(promotion);
        var loyaltyBenefit = await GetLoyaltyBenefitAsync(checkoutBranch.Id, verifiedSession?.Customer?.Id, cancellationToken);
        var availableBenefits = new List<PublicBenefitDto>();
        if (promotion is not null) availableBenefits.Add(ToBenefitDto(promotion));
        if (loyaltyBenefit is not null) availableBenefits.Add(loyaltyBenefit);
        var selectedBenefit = Normalize(request.BenefitSelection);
        var appliedBenefit = availableBenefits.Count switch
        {
            0 => null,
            1 => availableBenefits[0],
            _ => availableBenefits.FirstOrDefault(x => Normalize(x.Source) == selectedBenefit),
        };
        var benefitConflict = availableBenefits.Count > 1 && appliedBenefit is null;
        var applied = await ApplyBenefitAsync(cartLines, checkoutDeliveryFee, appliedBenefit, cancellationToken);
        cartLines = applied.Items;
        checkoutDeliveryFee = applied.DeliveryFee;
        var message = BuildWhatsAppMessage(
            request,
            fulfillmentType,
            normalizedCity,
            resolvedAddress,
            cartLines,
            subtotal,
            checkoutBranch,
            checkoutTravelMinutes,
            checkoutDeliveryFee,
            applied.DiscountTotal,
            appliedBenefit);
        if (message.Length > MaxWhatsAppMessageLength)
            return BadRequest(ApiResponse<PublicDeliveryQuoteDto>.ErrorResponse("El pedido contiene demasiada información. Reduce las notas e intenta nuevamente."));
        var whatsappUrl = $"{BuildWhatsAppUrl(checkoutBranch.ContactPhone)}?text={Uri.EscapeDataString(message)}";

        var onlinePaymentAvailable = await db.WompiPaymentIntegrations.AsNoTracking().AnyAsync(x =>
            x.TenantId == Math.Max(1, storefrontOptions.Value.TenantId)
            && x.BranchId == checkoutBranch.Id
            && x.IsEnabled
            && x.FinancialApp.Active
            && x.FinancialApp.Bank.Active
            && x.FinancialApp.Bank.BranchId == checkoutBranch.Id,
            cancellationToken);
        var result = new PublicDeliveryQuoteDto(
            fulfillmentType,
            resolvedAddress?.FormattedAddress,
            resolvedAddress?.Latitude,
            resolvedAddress?.Longitude,
            branchOptions,
            recommendedBranchId,
            selectedBranchId,
            checkoutBranch.Id,
            distanceMeters,
            checkoutDeliveryFee,
            travelMinutes,
            PreparationMinutes,
            fulfillmentType == "delivery" ? DeliveryPromiseMaxMinutes : PreparationMinutes,
            fulfillmentType == "delivery" ? DeliveryPromiseMinMinutes : PreparationMinutes,
            fulfillmentType == "delivery" ? DeliveryPromiseMaxMinutes : PreparationMinutes,
            outsideCoverage,
            cartLines,
            subtotal,
            applied.DiscountTotal,
            subtotal - applied.DiscountTotal + checkoutDeliveryFee,
            promotionDto,
            availableBenefits,
            appliedBenefit,
            benefitConflict,
            whatsappUrl,
            savedAddress is null ? "estimated" : "saved",
            onlinePaymentAvailable);

        return Ok(ApiResponse<PublicDeliveryQuoteDto>.SuccessResponse(result));
    }

    public Task<ActionResult<ApiResponse<PublicStorefrontOrderResult>>> ConfirmOrderTrusted(
        PublicStorefrontOrderRequest request,
        string idempotencyKey,
        StorefrontCustomerSessionResult customerSession,
        IMapper mapper,
        IOrderNotificationService notifications,
        ILogger<PublicStorefrontController> logger,
        string orderSource,
        int? whatsAppConversationId,
        CancellationToken cancellationToken)
        => ConfirmOrderCore(request, idempotencyKey, null, customerSession, mapper, notifications, logger, orderSource, whatsAppConversationId, cancellationToken);

    public async Task<ActionResult<ApiResponse<PublicStorefrontOrderResult>>> ConfirmOrderCore(
        PublicStorefrontOrderRequest request,
        string idempotencyKey,
        StorefrontCustomerAuthService? customerAuth,
        StorefrontCustomerSessionResult? trustedSession,
        IMapper mapper,
        IOrderNotificationService notifications,
        ILogger<PublicStorefrontController> logger,
        string orderSource,
        int? whatsAppConversationId,
        CancellationToken cancellationToken,
        string? sessionToken = null)
    {
        idempotencyKey = (idempotencyKey ?? string.Empty).Trim();
        if (idempotencyKey.Length is < 16 or > 80)
            return BadRequest(ApiResponse<PublicStorefrontOrderResult>.ErrorResponse("La confirmación del pedido no tiene una clave válida."));

        StorefrontCustomerSessionResult session;
        if (trustedSession is not null)
        {
            session = trustedSession;
        }
        else try
        {
            session = await customerAuth!.GetSessionAsync(sessionToken, cancellationToken);
        }
        catch (StorefrontAuthInvalidSessionException)
        {
            return Unauthorized(ApiResponse<PublicStorefrontOrderResult>.ErrorResponse("Verifica nuevamente tu celular para confirmar el pedido."));
        }
        if (session.AmbiguousCustomer)
            return Conflict(ApiResponse<PublicStorefrontOrderResult>.ErrorResponse("Este celular está asociado a más de un cliente. Comunícate con la sucursal para actualizar tus datos."));

        request.Phone = ColombianMobilePhone.Normalize(request.Phone);
        if (session.Phone != request.Phone)
            return Unauthorized(ApiResponse<PublicStorefrontOrderResult>.ErrorResponse("El celular del pedido no coincide con la sesión verificada."));
        var paymentMethod = (request.PaymentMethod ?? string.Empty).Trim().ToLowerInvariant();
        if (paymentMethod is not "cash" and not "online")
            return BadRequest(ApiResponse<PublicStorefrontOrderResult>.ErrorResponse("Selecciona efectivo o pago en línea."));

        var existingOrder = await db.Orders.AsNoTracking()
            .Include(x => x.Customer)
            .FirstOrDefaultAsync(x => x.StorefrontIdempotencyKey == idempotencyKey, cancellationToken);
        if (existingOrder is not null)
        {
            if (existingOrder.Customer is null || (existingOrder.Customer.Phone1 != session.Phone && existingOrder.Customer.Phone2 != session.Phone))
                return Conflict(ApiResponse<PublicStorefrontOrderResult>.ErrorResponse("La clave de confirmación ya fue utilizada."));
            return Ok(ApiResponse<PublicStorefrontOrderResult>.SuccessResponse(await ToPublicOrderResultAsync(existingOrder, cancellationToken)));
        }
        var existingCheckout = await db.StorefrontCheckouts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existingCheckout is not null)
        {
            if (existingCheckout.CustomerPhone != session.Phone)
                return Conflict(ApiResponse<PublicStorefrontOrderResult>.ErrorResponse("La clave de confirmación ya fue utilizada."));
            var payment = await wompi.GetCheckoutPaymentStatusAsync(
                Math.Max(1, storefrontOptions.Value.TenantId), existingCheckout.PublicId, cancellationToken);
            return Ok(ApiResponse<PublicStorefrontOrderResult>.SuccessResponse(ToPublicCheckoutResult(existingCheckout, payment)));
        }

        var quoteAction = await QuoteCore(request, cancellationToken, customerAuth, session);
        if (quoteAction.Result is not OkObjectResult ok
            || ok.Value is not ApiResponse<PublicDeliveryQuoteDto> quoteResponse
            || quoteResponse.Data is null)
        {
            if (quoteAction.Result is ObjectResult rejected)
                return new ObjectResult(rejected.Value) { StatusCode = rejected.StatusCode ?? StatusCodes.Status400BadRequest };
            return BadRequest(ApiResponse<PublicStorefrontOrderResult>.ErrorResponse("No fue posible validar nuevamente el pedido."));
        }
        var quote = quoteResponse.Data;
        if (quote.IsOutsideCoverage)
            return Conflict(ApiResponse<PublicStorefrontOrderResult>.ErrorResponse(
                "La dirección está fuera de cobertura. Cambia la dirección o selecciona recogida en sede."));
        if (quote.BenefitConflict)
            return Conflict(ApiResponse<PublicStorefrontOrderResult>.ErrorResponse("Elige entre la promoción del día y tu premio de fidelización antes de continuar."));

        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
        try
        {
            if (db.Database.IsRelational() && db.Database.CurrentTransaction is null)
                transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

            existingOrder = await db.Orders.Include(x => x.Customer)
                .FirstOrDefaultAsync(x => x.StorefrontIdempotencyKey == idempotencyKey, cancellationToken);
            if (existingOrder is not null)
            {
                if (existingOrder.Customer is null || (existingOrder.Customer.Phone1 != session.Phone && existingOrder.Customer.Phone2 != session.Phone))
                    throw new StorefrontOrderConflictException("La clave de confirmación ya fue utilizada.");
                if (transaction is not null)
                    await transaction.CommitAsync(cancellationToken);
                return Ok(ApiResponse<PublicStorefrontOrderResult>.SuccessResponse(await ToPublicOrderResultAsync(existingOrder, cancellationToken)));
            }
            existingCheckout = await db.StorefrontCheckouts
                .FirstOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, cancellationToken);
            if (existingCheckout is not null)
            {
                if (existingCheckout.CustomerPhone != session.Phone)
                    throw new StorefrontOrderConflictException("La clave de confirmación ya fue utilizada.");
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                var existingPayment = await wompi.GetCheckoutPaymentStatusAsync(
                    Math.Max(1, storefrontOptions.Value.TenantId), existingCheckout.PublicId, cancellationToken);
                return Ok(ApiResponse<PublicStorefrontOrderResult>.SuccessResponse(ToPublicCheckoutResult(existingCheckout, existingPayment)));
            }

            var branch = await db.Branches.FirstOrDefaultAsync(x => x.Id == quote.CheckoutBranchId && x.IsActive, cancellationToken);
            if (branch?.StorefrontTakenByUserId is null)
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    ApiResponse<PublicStorefrontOrderResult>.ErrorResponse("La sucursal todavía no está configurada para recibir pedidos directos desde la web."));
            var technicalUserIsValid = await db.Users.AsNoTracking().AnyAsync(
                x => x.Id == branch.StorefrontTakenByUserId && x.BranchId == branch.Id && x.Active, cancellationToken);
            if (!technicalUserIsValid)
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    ApiResponse<PublicStorefrontOrderResult>.ErrorResponse("El usuario técnico de pedidos web no está disponible para esta sucursal."));
            var wompiIntegration = paymentMethod == "online"
                ? await wompi.GetEnabledIntegrationAsync(Math.Max(1, storefrontOptions.Value.TenantId), branch.Id, cancellationToken)
                : null;
            if (paymentMethod == "online" && wompiIntegration is null)
                return Conflict(ApiResponse<PublicStorefrontOrderResult>.ErrorResponse("Esta sucursal no tiene pago en línea disponible. Selecciona efectivo o elige otra sucursal."));

            if (paymentMethod == "online")
            {
                var checkout = new StorefrontCheckout
                {
                    TenantId = Math.Max(1, storefrontOptions.Value.TenantId),
                    PublicId = Guid.NewGuid().ToString("N"),
                    IdempotencyKey = idempotencyKey,
                    BranchId = branch.Id,
                    CustomerId = session.Customer?.Id,
                    SavedAddressId = request.SavedAddressId,
                    WhatsAppConversationId = whatsAppConversationId,
                    OrderSource = orderSource,
                    CustomerPhone = session.Phone,
                    CustomerName = request.Name.Trim(),
                    FulfillmentType = quote.FulfillmentType,
                    AddressLabel = request.AddressLabel?.Trim(),
                    OriginalAddress = request.Address,
                    FormattedAddress = quote.FormattedAddress,
                    AddressAdditionalInfo = request.AddressAdditionalInfo?.Trim(),
                    Latitude = quote.Latitude,
                    Longitude = quote.Longitude,
                    DeliveryFee = quote.Total - (quote.Subtotal - quote.DiscountTotal),
                    Subtotal = quote.Subtotal,
                    DiscountTotal = quote.DiscountTotal,
                    Total = quote.Total,
                    ItemsJson = JsonSerializer.Serialize(quote.Items.Select(x => new StorefrontCheckoutLine(
                        x.ProductId, x.Quantity, x.UnitPrice, x.Discount, x.Subtotal, x.Notes))),
                    OrderNotes = request.OrderNotes?.Trim(),
                    AppliedBenefitType = ParseBenefitType(quote.AppliedBenefit?.Source),
                    AppliedBenefitSourceId = quote.AppliedBenefit?.SourceId,
                    AppliedBenefitLabel = quote.AppliedBenefit?.Title,
                    AppliedBenefitRewardType = ParseRewardType(quote.AppliedBenefit?.RewardType),
                    AppliedBenefitAmount = quote.AppliedBenefit?.DiscountPercentage,
                    AppliedBenefitSnapshot = quote.AppliedBenefit?.Snapshot,
                    Status = "pending",
                    ExpiresAt = clock.UtcNow.AddMinutes(15),
                };
                db.StorefrontCheckouts.Add(checkout);
                await db.SaveChangesAsync(cancellationToken);
                var wompiCheckout = wompi.CreateCheckoutAttempt(checkout, wompiIntegration!, clock.UtcNow);
                await db.SaveChangesAsync(cancellationToken);
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                return Ok(ApiResponse<PublicStorefrontOrderResult>.SuccessResponse(
                    new(null, checkout.PublicId, "PendingPayment", checkout.BranchId, checkout.Subtotal,
                        checkout.DeliveryFee, checkout.Total, "online", "Pending", wompiCheckout)));
            }

            Customer customer;
            if (session.Customer is not null)
            {
                customer = await db.Customers.FirstOrDefaultAsync(x => x.Id == session.Customer.Id && x.Active, cancellationToken)
                    ?? throw new StorefrontOrderConflictException("El cliente verificado ya no está disponible.");
            }
            else
            {
                var matches = await db.Customers.Where(x => x.Active && (x.Phone1 == session.Phone || x.Phone2 == session.Phone))
                    .OrderBy(x => x.Id).Take(2).ToListAsync(cancellationToken);
                if (matches.Count > 1)
                    throw new StorefrontOrderConflictException("El celular quedó asociado a más de un cliente.");
                customer = matches.SingleOrDefault() ?? new Customer
                {
                    BranchId = branch.Id,
                    Name = request.Name.Trim(),
                    Phone1 = session.Phone,
                    Active = true
                };
                if (customer.Id == 0)
                    db.Customers.Add(customer);
            }

            Address? address = null;
            if (quote.FulfillmentType == "delivery")
            {
                if (request.SavedAddressId.HasValue)
                {
                    address = await db.Addresses.FirstOrDefaultAsync(
                        x => x.Id == request.SavedAddressId && x.CustomerId == customer.Id, cancellationToken)
                        ?? throw new StorefrontOrderConflictException("La dirección guardada ya no pertenece al cliente verificado.");
                }
                else
                {
                    var hasAddresses = customer.Id != 0 && await db.Addresses.AnyAsync(x => x.CustomerId == customer.Id, cancellationToken);
                    address = new Address
                    {
                        Customer = customer,
                        Label = string.IsNullOrWhiteSpace(request.AddressLabel) ? "Casa" : request.AddressLabel.Trim(),
                        AddressText = quote.FormattedAddress!,
                        AdditionalInfo = request.AddressAdditionalInfo?.Trim(),
                        DeliveryFee = quote.Total - (quote.Subtotal - quote.DiscountTotal),
                        Latitude = quote.Latitude,
                        Longitude = quote.Longitude,
                        IsPrimary = !hasAddresses,
                        OriginalAddressText = request.Address,
                        NormalizedAddressText = quote.FormattedAddress,
                        ValidationSource = "storefront_google",
                        ValidatedAt = clock.UtcNow
                    };
                    db.Addresses.Add(address);
                }
            }

            var order = new Order
            {
                BranchId = branch.Id,
                TakenById = branch.StorefrontTakenByUserId.Value,
                Customer = customer,
                Address = address,
                GuestName = request.Name.Trim(),
                Type = quote.FulfillmentType == "delivery" ? OrderType.Delivery : OrderType.Onsite,
                DeliveryFee = quote.FulfillmentType == "delivery" ? quote.Total - (quote.Subtotal - quote.DiscountTotal) : 0,
                Status = OrderStatus.Taken,
                OrderSource = orderSource,
                WhatsAppConversationId = whatsAppConversationId,
                StorefrontIdempotencyKey = idempotencyKey,
                Notes = request.OrderNotes?.Trim(),
                AppliedBenefitType = ParseBenefitType(quote.AppliedBenefit?.Source),
                AppliedBenefitSourceId = quote.AppliedBenefit?.SourceId,
                AppliedBenefitLabel = quote.AppliedBenefit?.Title,
                AppliedBenefitRewardType = ParseRewardType(quote.AppliedBenefit?.RewardType),
                AppliedBenefitAmount = quote.AppliedBenefit?.DiscountPercentage,
                AppliedBenefitSnapshot = quote.AppliedBenefit?.Snapshot,
                FreeDeliveryRequested = quote.AppliedBenefit?.RewardType == nameof(LoyaltyRewardType.FreeDelivery),
            };
            order.AddStatusTime(order.Status, clock.UtcNow);
            foreach (var line in quote.Items)
            {
                order.OrderDetails.Add(new OrderDetail
                {
                    ProductId = line.ProductId,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    Discount = line.Discount,
                    Subtotal = line.Subtotal,
                    Notes = line.Notes
                });
            }
            OrderTotalsHelper.RecalculateFromOrderDetails(order);
            db.Orders.Add(order);
            await db.SaveChangesAsync(cancellationToken);
            db.PaymentNotificationOutboxMessages.Add(new PaymentNotificationOutboxMessage
            {
                TenantId = Math.Max(1, storefrontOptions.Value.TenantId),
                BranchId = order.BranchId,
                Order = order,
                EventType = "order_created_web_cash",
                Status = "pending",
                NextAttemptAt = clock.UtcNow,
            });
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            paymentNotificationSignal?.Pulse();

            return Ok(ApiResponse<PublicStorefrontOrderResult>.SuccessResponse(ToPublicOrderResult(order, paymentMethod, null)));
        }
        catch (StorefrontOrderConflictException ex)
        {
            if (transaction is not null)
                await transaction.RollbackAsync(cancellationToken);
            return Conflict(ApiResponse<PublicStorefrontOrderResult>.ErrorResponse(ex.Message));
        }
        catch (DbUpdateException) when (transaction is not null || !db.Database.IsRelational())
        {
            if (transaction is not null)
                await transaction.RollbackAsync(cancellationToken);
            existingOrder = await db.Orders.AsNoTracking().Include(x => x.Customer)
                .FirstOrDefaultAsync(x => x.StorefrontIdempotencyKey == idempotencyKey, cancellationToken);
            if (existingOrder is not null)
            {
                if (existingOrder.Customer is null || (existingOrder.Customer.Phone1 != session.Phone && existingOrder.Customer.Phone2 != session.Phone))
                    return Conflict(ApiResponse<PublicStorefrontOrderResult>.ErrorResponse("La clave de confirmación ya fue utilizada."));
                return Ok(ApiResponse<PublicStorefrontOrderResult>.SuccessResponse(await ToPublicOrderResultAsync(existingOrder, cancellationToken)));
            }
            existingCheckout = await db.StorefrontCheckouts.AsNoTracking()
                .FirstOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, cancellationToken);
            if (existingCheckout is not null)
            {
                if (existingCheckout.CustomerPhone != session.Phone)
                    return Conflict(ApiResponse<PublicStorefrontOrderResult>.ErrorResponse("La clave de confirmación ya fue utilizada."));
                var payment = await wompi.GetCheckoutPaymentStatusAsync(
                    Math.Max(1, storefrontOptions.Value.TenantId), existingCheckout.PublicId, cancellationToken);
                return Ok(ApiResponse<PublicStorefrontOrderResult>.SuccessResponse(ToPublicCheckoutResult(existingCheckout, payment)));
            }
            throw;
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }
    }

    private async Task<PublicStorefrontOrderResult> ToPublicOrderResultAsync(Order order, CancellationToken cancellationToken)
    {
        var payment = await wompi.GetOrderPaymentStatusAsync(Math.Max(1, storefrontOptions.Value.TenantId), order.Id, cancellationToken);
        return ToPublicOrderResult(order, payment is null ? "cash" : "online", payment?.Checkout, payment?.PaymentStatus);
    }

    private static PublicStorefrontOrderResult ToPublicOrderResult(Order order, string paymentMethod, WompiCheckoutData? checkout, string? paymentStatus = null) => new(
        order.Id,
        null,
        order.Status.ToString(),
        order.BranchId,
        order.Subtotal,
        order.DeliveryFee ?? 0,
        order.Total,
        paymentMethod,
        paymentStatus ?? (paymentMethod == "cash" ? "NotRequired" : "Pending"),
        checkout);

    private static PublicStorefrontOrderResult ToPublicCheckoutResult(
        StorefrontCheckout checkout,
        WompiStorefrontCheckoutStatusResult? payment) => new(
        payment?.OrderId ?? checkout.OrderId,
        checkout.PublicId,
        checkout.Status,
        checkout.BranchId,
        checkout.Subtotal,
        checkout.DeliveryFee,
        checkout.Total,
        "online",
        payment?.PaymentStatus ?? "Pending",
        payment?.Checkout);

    private static OrderBenefitType ParseBenefitType(string? source) => Normalize(source) switch
    {
        "daily_promotion" => OrderBenefitType.DailyPromotion,
        "loyalty" => OrderBenefitType.Loyalty,
        _ => OrderBenefitType.None,
    };

    private static LoyaltyRewardType? ParseRewardType(string? value) =>
        Enum.TryParse<LoyaltyRewardType>(value, true, out var parsed) ? parsed : null;

    private IQueryable<Branch> EligibleBranchesQuery() => db.Branches
        .AsNoTracking()
        .Where(x => x.IsActive
            && x.Latitude.HasValue
            && x.Longitude.HasValue
            && (x.Phone1.Trim() != "" || (x.Phone2 != null && x.Phone2.Trim() != "")));

    private Task<List<BranchRouteSource>> GetEligibleBranches(CancellationToken cancellationToken) => EligibleBranchesQuery()
        .Select(x => new BranchRouteSource(
            x.Id,
            x.Name,
            x.Address,
            x.Latitude!.Value,
            x.Longitude!.Value,
            x.Phone1.Trim() != "" ? x.Phone1 : x.Phone2!))
        .ToListAsync(cancellationToken);

    private async Task<List<BranchRouteResult>> GetValidRoutes(
        IReadOnlyCollection<BranchRouteSource> branches,
        (double Latitude, double Longitude) destination,
        CancellationToken cancellationToken)
    {
        var routeTasks = branches.Select(async branch =>
            new BranchRouteResult(branch, await RouteCached(branch, destination, cancellationToken)));
        return (await Task.WhenAll(routeTasks))
            .Where(x => x.Metrics.DurationSeconds > 0)
            .OrderBy(x => x.Metrics.DurationSeconds)
            .ToList();
    }

    private async Task<(GeocodedAddress? Result, string? Error)> ResolveAddressCached(
        string address,
        decimal? latitude,
        decimal? longitude,
        CancellationToken cancellationToken)
    {
        var locationSource = latitude.HasValue && longitude.HasValue
            ? $"{latitude.Value:F6}|{longitude.Value:F6}"
            : Normalize(address);
        var cacheKey = $"storefront:geocode:{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(locationSource)))}";
        if (cache.TryGetValue(cacheKey, out (GeocodedAddress? Result, string? Error) cached))
            return cached;

        var resolved = await geocoder.Resolve(address, latitude, longitude, cancellationToken);
        if (resolved.Result is not null)
            cache.Set(cacheKey, resolved, MapCacheDuration);
        return resolved;
    }

    private async Task<DrivingRouteMetrics> RouteCached(
        BranchRouteSource branch,
        (double Latitude, double Longitude) destination,
        CancellationToken cancellationToken)
    {
        var cacheKey = FormattableString.Invariant(
            $"storefront:route:{branch.Id}:{branch.Latitude:F6}:{branch.Longitude:F6}:{destination.Latitude:F6}:{destination.Longitude:F6}");
        if (cache.TryGetValue(cacheKey, out DrivingRouteMetrics cached))
            return cached;

        var metrics = await routes.ComputeRouteAsync(
            [((double)branch.Latitude, (double)branch.Longitude), destination],
            cancellationToken);
        if (metrics.DurationSeconds > 0)
            cache.Set(cacheKey, metrics, MapCacheDuration);
        return metrics;
    }

    private async Task<DailyPromotion?> GetActivePromotion(int branchId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        return await db.DailyPromotions
            .AsNoTracking()
            .Include(x => x.Branch)
            .Include(x => x.GiftProduct)
            .Include(x => x.DiscountProducts)
                .ThenInclude(x => x.Product)
            .Where(x => x.BranchId == branchId
                && x.IsActive
                && x.StartsAt <= now
                && (x.EndsAt == null || x.EndsAt > now))
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<PublicBenefitDto?> GetLoyaltyBenefitAsync(
        int branchId,
        int? customerId,
        CancellationToken cancellationToken)
    {
        if (!customerId.HasValue) return null;
        var hasReservedReward = await db.Orders.AsNoTracking().AnyAsync(
                x => x.CustomerId == customerId
                    && x.AppliedBenefitType == OrderBenefitType.Loyalty
                    && x.Status != OrderStatus.Cancelled
                    && x.Status != OrderStatus.Delivered,
                cancellationToken)
            || await db.StorefrontCheckouts.AsNoTracking().AnyAsync(
                x => x.CustomerId == customerId
                    && x.AppliedBenefitType == OrderBenefitType.Loyalty
                    && x.Status == "pending"
                    && x.ExpiresAt > clock.UtcNow,
                cancellationToken);
        if (hasReservedReward) return null;
        var delivered = await db.Orders.AsNoTracking().CountAsync(
            x => x.CustomerId == customerId && x.Status == OrderStatus.Delivered,
            cancellationToken);
        if (LoyaltyDeliveriesPerReward.GetDeliveriesUntilNextReward(delivered) != 1) return null;
        var cycleLength = await db.LoyaltyCycleSteps.AsNoTracking()
            .Where(x => x.BranchId == branchId && x.IsActive)
            .Select(x => (int?)x.StepIndex)
            .MaxAsync(cancellationToken) ?? 0;
        if (cycleLength <= 0) return null;
        var stepIndex = LoyaltyDeliveriesPerReward.GetStepIndexAtMilestone(delivered + 1, cycleLength);
        var step = await db.LoyaltyCycleSteps.AsNoTracking()
            .Include(x => x.GiftProduct)
            .FirstOrDefaultAsync(x => x.BranchId == branchId && x.StepIndex == stepIndex && x.IsActive, cancellationToken);
        if (step?.RewardType is null) return null;
        var snapshot = JsonSerializer.Serialize(new
        {
            step.Id,
            step.StepIndex,
            step.RewardLabel,
            RewardType = step.RewardType.ToString(),
            step.GiftProductId,
            GiftProductName = step.GiftProduct?.Name,
            step.DiscountPercentage,
            DeliveredOrders = delivered,
        });
        return new(
            "loyalty",
            step.Id,
            string.IsNullOrWhiteSpace(step.RewardLabel) ? "Premio de fidelización" : step.RewardLabel,
            step.RewardType.Value.ToString(),
            step.GiftProductId,
            step.GiftProduct?.Name,
            step.DiscountPercentage,
            null,
            [],
            snapshot);
    }

    private static PublicBenefitDto ToBenefitDto(DailyPromotion promotion)
    {
        var dto = ToPromotionDto(promotion);
        var snapshot = JsonSerializer.Serialize(new
        {
            promotion.Id,
            promotion.BranchId,
            Type = promotion.Type.ToString(),
            dto.Title,
            promotion.MinimumOrderValue,
            promotion.GiftProductId,
            dto.GiftProductName,
            promotion.DiscountPercentage,
            DiscountScope = promotion.DiscountScope?.ToString(),
            DiscountProductIds = dto.DiscountProductIds,
            promotion.StartsAt,
            promotion.EndsAt,
        });
        return new(
            "daily_promotion",
            promotion.Id,
            dto.Title,
            promotion.Type.ToString(),
            promotion.GiftProductId,
            dto.GiftProductName,
            promotion.DiscountPercentage,
            promotion.DiscountScope?.ToString(),
            dto.DiscountProductIds,
            snapshot);
    }

    private async Task<BenefitApplication> ApplyBenefitAsync(
        IReadOnlyCollection<PublicCartLineDto> sourceItems,
        int deliveryFee,
        PublicBenefitDto? benefit,
        CancellationToken cancellationToken)
    {
        var items = sourceItems.Select(x => x with { Discount = 0, Subtotal = x.Quantity * x.UnitPrice }).ToList();
        if (benefit is null) return new(items, deliveryFee, 0);

        if (benefit.RewardType == nameof(LoyaltyRewardType.GiftProduct) && benefit.GiftProductId.HasValue)
        {
            var gift = await db.Products.AsNoTracking().FirstOrDefaultAsync(
                x => x.Id == benefit.GiftProductId && x.Active,
                cancellationToken);
            if (gift is not null)
                items.Add(new(gift.Id, gift.Name, 1, 0, 0, benefit.Source == "loyalty" ? "Regalo de fidelización" : "Promoción del día", 0, true));
            return new(items, deliveryFee, 0);
        }

        if (benefit.RewardType == nameof(LoyaltyRewardType.FreeDelivery))
            return new(items, 0, 0);

        if (benefit.RewardType == nameof(LoyaltyRewardType.PercentageDiscount))
        {
            var percentage = Math.Clamp(benefit.DiscountPercentage ?? 0, 0, 100);
            var eligibleIds = benefit.DiscountScope == nameof(DailyPromotionDiscountScope.SpecificProducts)
                ? benefit.DiscountProductIds.ToHashSet()
                : null;
            items = items.Select(item =>
            {
                if (eligibleIds is not null && !eligibleIds.Contains(item.ProductId)) return item;
                var gross = item.Quantity * item.UnitPrice;
                var discount = (int)Math.Round(gross * percentage / 100m, MidpointRounding.AwayFromZero);
                return item with { Discount = discount, Subtotal = gross - discount };
            }).ToList();
        }

        return new(items, deliveryFee, items.Sum(x => x.Discount));
    }

    private sealed record BenefitApplication(List<PublicCartLineDto> Items, int DeliveryFee, int DiscountTotal);

    private static string? ValidateItems(
        IReadOnlyCollection<PublicCartItemRequest> items,
        IReadOnlyDictionary<int, Product> products)
    {
        if (items.Count == 0)
            return "Agrega al menos un producto al carrito.";
        if (items.Count > 30)
            return "El carrito supera la cantidad máxima de productos distintos.";
        var hasMain = false;
        foreach (var item in items)
        {
            if (item.Quantity is < 1 or > 50)
                return "Cada cantidad debe estar entre 1 y 50.";
            if (!products.TryGetValue(item.ProductId, out var product) || !product.Active)
                return "Uno de los productos ya no está disponible.";
            if (!PublicRoles.Contains(product.Category.StorefrontRole))
                return "Uno de los productos no está habilitado para pedidos web.";
            if (product.Stock.HasValue && product.Stock.Value < item.Quantity)
                return $"No hay stock suficiente de {product.Name}.";
            hasMain |= MainRoles.Contains(product.Category.StorefrontRole);
        }
        return hasMain ? null : "Agrega al menos un arroz o combo para continuar.";
    }

    private static IReadOnlyCollection<PublicProductGroupDto> BuildGroups(
        IReadOnlyCollection<Product> products,
        string role) => products
        .Where(x => x.Category.StorefrontRole == role)
        .GroupBy(x => x.CommercialProfileId.HasValue
            ? $"{role}:profile:{x.CommercialProfileId.Value}"
            : $"{role}:product:{x.Id}")
        .Select(group =>
        {
            var first = group.OrderBy(x => x.StorefrontSortOrder).ThenBy(x => x.Name).First();
            var options = group
                .OrderBy(x => x.Price)
                .ThenBy(x => x.StorefrontSortOrder)
                .ThenBy(x => x.Name)
                .Select(x => new PublicProductOptionDto(
                    x.Id,
                    x.Name,
                    x.StorefrontVariantLabel ?? x.Name,
                    x.Price,
                    Availability(x),
                    x.ServesPeopleMin,
                    x.ServesPeopleMax))
                .ToList();
            return new PublicProductGroupDto(
                group.Key,
                first.CategoryId,
                first.Category.Name,
                first.CommercialProfile?.Name ?? (role == "rice" ? first.Category.Name : first.Name),
                first.CommercialProfile?.Description,
                first.CommercialProfile?.Ingredients,
                first.CommercialProfile?.PhotoUrl,
                group.Min(x => x.StorefrontSortOrder),
                options);
        })
        .OrderBy(x => x.SortOrder)
        .ThenBy(x => x.Name)
        .ToList();

    private static string Availability(Product product) => product.Stock.HasValue && product.Stock.Value <= 0
        ? "unavailable"
        : product.Stock.HasValue && product.Stock.Value <= 5
            ? "lowStock"
            : "available";

    private static PublicPromotionDto ToPromotionDto(DailyPromotion promotion)
    {
        var title = promotion.Type switch
        {
            DailyPromotionType.GiftProduct => $"{promotion.GiftProduct?.Name ?? "Producto"} de regalo",
            DailyPromotionType.FreeDelivery => "Domicilio gratis",
            DailyPromotionType.PercentageDiscount => $"{promotion.DiscountPercentage:0.#}% de descuento",
            _ => "Promoción activa"
        };
        return new(
            promotion.Id,
            promotion.BranchId,
            promotion.Branch.Name,
            promotion.Type.ToString(),
            title,
            promotion.MinimumOrderValue,
            promotion.GiftProductId,
            promotion.GiftProduct?.Name,
            promotion.DiscountPercentage,
            promotion.DiscountScope?.ToString(),
            promotion.DiscountProducts.Select(x => x.ProductId).ToList(),
            promotion.EndsAt);
    }

    private static string BuildWhatsAppMessage(
        PublicDeliveryQuoteRequest request,
        string fulfillmentType,
        string? city,
        GeocodedAddress? resolvedAddress,
        IReadOnlyCollection<PublicCartLineDto> lines,
        int subtotal,
        BranchRouteSource branch,
        int travelMinutes,
        int estimatedDeliveryFee,
        int discountTotal,
        PublicBenefitDto? appliedBenefit)
    {
        var sb = new StringBuilder();
        sb.AppendLine("*NUEVO PEDIDO WEB*");
        sb.AppendLine();
        sb.AppendLine($"*Cliente:* {SingleLine(request.Name)}");
        sb.AppendLine($"*Teléfono:* {ColombianMobilePhone.Normalize(request.Phone)}");
        if (fulfillmentType == "pickup")
        {
            sb.AppendLine("*Modalidad:* Recoger en el local");
            sb.AppendLine($"*Sucursal:* {branch.Name}");
            sb.AppendLine($"*Dirección de recogida:* {branch.Address}");
            sb.AppendLine($"*Ubicación de la sede:* {GoogleMapsUrl(branch.Latitude, branch.Longitude)}");
            sb.AppendLine($"*Tiempo estimado de preparación:* {PreparationMinutes} min");
        }
        else
        {
            sb.AppendLine("*Modalidad:* Domicilio");
            sb.AppendLine($"*Ciudad:* {city}");
            sb.AppendLine($"*Dirección confirmada:* {resolvedAddress!.FormattedAddress}");
            if (!string.IsNullOrWhiteSpace(request.AddressAdditionalInfo))
                sb.AppendLine($"*Datos adicionales:* {SingleLine(request.AddressAdditionalInfo)}");
            sb.AppendLine($"*Ubicación:* {GoogleMapsUrl(resolvedAddress.Latitude, resolvedAddress.Longitude)}");
            sb.AppendLine($"*Sucursal:* {branch.Name}");
            sb.AppendLine($"*Tiempo estimado de entrega:* {DeliveryPromiseMinMinutes}–{DeliveryPromiseMaxMinutes} min");
            sb.AppendLine($"*Valor estimado del domicilio:* {Money(estimatedDeliveryFee)} (sujeto a confirmación de la sucursal)");
        }
        sb.AppendLine();
        sb.AppendLine("*Pedido:*");
        foreach (var line in lines)
        {
            sb.AppendLine($"• {line.Quantity} × {line.Name} — {Money(line.Subtotal)}");
            if (!string.IsNullOrWhiteSpace(line.Notes))
                sb.AppendLine($"  Nota: {SingleLine(line.Notes)}");
        }
        sb.AppendLine($"*Subtotal:* {Money(subtotal)}");
        if (discountTotal > 0)
            sb.AppendLine($"*Descuento:* -{Money(discountTotal)}");
        if (fulfillmentType == "delivery")
        {
            sb.AppendLine($"*Domicilio:* {Money(estimatedDeliveryFee)}");
            sb.AppendLine($"*Total estimado:* {Money(subtotal - discountTotal + estimatedDeliveryFee)}");
        }
        else
        {
            sb.AppendLine($"*Total:* {Money(subtotal - discountTotal)}");
        }
        if (appliedBenefit is not null)
            sb.AppendLine($"*Beneficio aplicado:* {appliedBenefit.Title}");
        return sb.ToString().Trim();
    }

    private static bool AddressMatchesCity(string formattedAddress, string city) =>
        Normalize(formattedAddress).Contains(Normalize(city), StringComparison.Ordinal);

    private static string? MatchAllowedCity(string? city)
    {
        var normalized = Normalize(city);
        return AllowedCities.FirstOrDefault(x => Normalize(x) == normalized);
    }

    private static string Normalize(string? value)
    {
        var source = (value ?? string.Empty).Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        return new string(source.Where(x => CharUnicodeInfo.GetUnicodeCategory(x) != UnicodeCategory.NonSpacingMark).ToArray())
            .Normalize(NormalizationForm.FormC);
    }

    private static string WhatsAppDigits(string? value)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        return digits.Length == 10 ? $"57{digits}" : digits;
    }
    private static string BuildWhatsAppUrl(string phone) => $"https://wa.me/{WhatsAppDigits(phone)}";

    private static string ClosedBranchMessage(string branchName, DateTime? nextOpeningAtUtc) => nextOpeningAtUtc.HasValue
        ? $"{branchName} está fuera de su horario de atención. Vuelve a recibir pedidos {FormatOpening(nextOpeningAtUtc.Value)}."
        : $"{branchName} está fuera de su horario de atención y no tiene una próxima apertura disponible.";

    private static string FormatOpening(DateTime openingAtUtc)
    {
        var local = ColombiaTimeHelper.GetNowInColombiaFromUtc(openingAtUtc);
        return local.ToString("dddd d 'de' MMMM 'a las' h:mm tt", CultureInfo.GetCultureInfo("es-CO"));
    }
    private static int ToMinutes(int durationSeconds) => (int)Math.Ceiling(durationSeconds / 60m);
    private static bool IsWithinCoverage(DrivingRouteMetrics metrics) =>
        metrics.DistanceMeters <= CoverageDistanceMeters && ToMinutes(metrics.DurationSeconds) <= CoverageTravelMinutes;
    private static int EstimateDeliveryFee(int distanceMeters) => DeliveryBaseFee
        + Math.Max(0, (int)Math.Ceiling((distanceMeters - DeliveryBaseDistanceMeters) / 1_000m)) * DeliveryFeePerAdditionalKilometer;
    private static PublicBranchQuoteDto ToBranchQuote(BranchRouteResult route, int recommendedBranchId, int selectedBranchId)
    {
        var travelMinutes = ToMinutes(route.Metrics.DurationSeconds);
        return new PublicBranchQuoteDto(
            route.Branch.Id,
            route.Branch.Name,
            route.Branch.Address,
            route.Branch.Latitude,
            route.Branch.Longitude,
            route.Metrics.DistanceMeters,
            EstimateDeliveryFee(route.Metrics.DistanceMeters),
            PreparationMinutes + travelMinutes,
            travelMinutes,
            IsWithinCoverage(route.Metrics),
            route.Branch.Id == recommendedBranchId,
            route.Branch.Id == selectedBranchId,
            route.Metrics.EncodedPolyline);
    }
    private static string Money(int value) => value.ToString("C0", CultureInfo.GetCultureInfo("es-CO"));
    private static string GoogleMapsUrl(decimal latitude, decimal longitude) =>
        $"https://www.google.com/maps?q={latitude.ToString(CultureInfo.InvariantCulture)},{longitude.ToString(CultureInfo.InvariantCulture)}";
    private static string SingleLine(string? value) => string.Join(' ', (value ?? string.Empty)
        .Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    private sealed record BranchRouteSource(
        int Id,
        string Name,
        string Address,
        decimal Latitude,
        decimal Longitude,
        string ContactPhone);
    private sealed record BranchRouteResult(BranchRouteSource Branch, DrivingRouteMetrics Metrics);
}
