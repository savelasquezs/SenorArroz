using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Globalization;
using System.Text;
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
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Services;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = StorefrontApiKeyOptions.Scheme)]
[Route("api/public/storefront")]
public sealed class PublicStorefrontController(
    IApplicationDbContext db,
    IGoogleRoutesDrivingMetricsService routes,
    GoogleAddressGeocoder geocoder,
    IClock clock,
    IBranchBusinessHoursService businessHours,
    IMemoryCache cache,
    StorefrontQuoteConcurrencyGate concurrencyGate,
    IWompiPaymentService wompi,
    IOptions<StorefrontCustomerAuthOptions> storefrontOptions) : ControllerBase
{
    private const int PreparationMinutes = 20;
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

    [HttpGet("catalog")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [EnableRateLimiting("storefront-catalog")]
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

    [HttpPost("address-preview")]
    [RequestSizeLimit(4 * 1024)]
    [EnableRateLimiting("storefront-quote")]
    public async Task<ActionResult<ApiResponse<PublicAddressPreviewDto>>> PreviewAddress(
        [FromBody] PublicAddressPreviewRequest request,
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

    [HttpPost("coverage-preview")]
    [RequestSizeLimit(8 * 1024)]
    [EnableRateLimiting("storefront-quote")]
    public async Task<ActionResult<ApiResponse<PublicCoveragePreviewDto>>> PreviewCoverage(
        [FromBody] PublicCoveragePreviewRequest request,
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

    [HttpPost("delivery-quote")]
    [RequestSizeLimit(32 * 1024)]
    [EnableRateLimiting("storefront-quote")]
    public async Task<ActionResult<ApiResponse<PublicDeliveryQuoteDto>>> Quote(
        [FromBody] PublicDeliveryQuoteRequest request,
        CancellationToken cancellationToken,
        [FromServices] StorefrontCustomerAuthService? customerAuth = null)
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

        Address? savedAddress = null;
        if (request.SavedAddressId.HasValue)
        {
            if (customerAuth is null)
                return Unauthorized(ApiResponse<PublicDeliveryQuoteDto>.ErrorResponse("Verifica tu celular para usar una dirección guardada."));
            StorefrontCustomerSessionResult session;
            try
            {
                session = await customerAuth.GetSessionAsync(Request.Headers["X-Storefront-Customer-Session"].FirstOrDefault(), cancellationToken);
            }
            catch (StorefrontAuthInvalidSessionException)
            {
                return Unauthorized(ApiResponse<PublicDeliveryQuoteDto>.ErrorResponse("La sesión del cliente no es válida o venció."));
            }
            if (session.Phone != request.Phone || session.Customer is null || session.AmbiguousCustomer)
                return Unauthorized(ApiResponse<PublicDeliveryQuoteDto>.ErrorResponse("La dirección no pertenece a la sesión verificada."));
            savedAddress = await db.Addresses.AsNoTracking().FirstOrDefaultAsync(
                x => x.Id == request.SavedAddressId && x.CustomerId == session.Customer.Id, cancellationToken);
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
        var promotionDto = promotion is null ? null : ToPromotionDto(promotion);
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
            promotionDto);
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
            estimatedDeliveryFee,
            travelMinutes,
            PreparationMinutes,
            PreparationMinutes + travelMinutes,
            outsideCoverage,
            cartLines,
            subtotal,
            subtotal + checkoutDeliveryFee,
            promotionDto,
            whatsappUrl,
            savedAddress is null ? "estimated" : "saved",
            onlinePaymentAvailable);

        return Ok(ApiResponse<PublicDeliveryQuoteDto>.SuccessResponse(result));
    }

    [HttpPost("orders")]
    [RequestSizeLimit(32 * 1024)]
    [EnableRateLimiting("storefront-quote")]
    public async Task<ActionResult<ApiResponse<PublicStorefrontOrderResult>>> ConfirmOrder(
        [FromBody] PublicStorefrontOrderRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        [FromServices] StorefrontCustomerAuthService customerAuth,
        [FromServices] IMapper mapper,
        [FromServices] IOrderNotificationService notifications,
        [FromServices] ILogger<PublicStorefrontController> logger,
        CancellationToken cancellationToken)
    {
        idempotencyKey = (idempotencyKey ?? string.Empty).Trim();
        if (idempotencyKey.Length is < 16 or > 80)
            return BadRequest(ApiResponse<PublicStorefrontOrderResult>.ErrorResponse("La confirmación del pedido no tiene una clave válida."));

        StorefrontCustomerSessionResult session;
        try
        {
            session = await customerAuth.GetSessionAsync(Request.Headers["X-Storefront-Customer-Session"].FirstOrDefault(), cancellationToken);
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

        var quoteAction = await Quote(request, cancellationToken, customerAuth);
        if (quoteAction.Result is not OkObjectResult ok
            || ok.Value is not ApiResponse<PublicDeliveryQuoteDto> quoteResponse
            || quoteResponse.Data is null)
        {
            if (quoteAction.Result is ObjectResult rejected)
                return new ObjectResult(rejected.Value) { StatusCode = rejected.StatusCode ?? StatusCodes.Status400BadRequest };
            return BadRequest(ApiResponse<PublicStorefrontOrderResult>.ErrorResponse("No fue posible validar nuevamente el pedido."));
        }
        var quote = quoteResponse.Data;

        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
        try
        {
            if (db.Database.IsRelational())
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
                        DeliveryFee = quote.Total - quote.Subtotal,
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
                DeliveryFee = quote.FulfillmentType == "delivery" ? quote.Total - quote.Subtotal : 0,
                Status = paymentMethod == "online" ? OrderStatus.AwaitingPayment : OrderStatus.Taken,
                OrderSource = "web",
                StorefrontIdempotencyKey = idempotencyKey,
                Notes = request.OrderNotes?.Trim()
            };
            order.AddStatusTime(order.Status, clock.UtcNow);
            foreach (var line in quote.Items)
            {
                order.OrderDetails.Add(new OrderDetail
                {
                    ProductId = line.ProductId,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    Discount = 0,
                    Subtotal = line.Subtotal,
                    Notes = line.Notes
                });
            }
            OrderTotalsHelper.RecalculateFromOrderDetails(order);
            db.Orders.Add(order);
            await db.SaveChangesAsync(cancellationToken);
            WompiCheckoutData? checkout = null;
            if (wompiIntegration is not null)
            {
                checkout = wompi.CreateAttempt(order, wompiIntegration, clock.UtcNow);
                await db.SaveChangesAsync(cancellationToken);
            }
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);

            if (paymentMethod == "cash") try
            {
                var notificationOrder = await db.Orders.AsNoTracking()
                    .Include(x => x.Branch)
                    .Include(x => x.TakenBy)
                    .Include(x => x.Customer)
                    .Include(x => x.Address).ThenInclude(x => x!.Neighborhood)
                    .Include(x => x.OrderDetails).ThenInclude(x => x.Product)
                    .FirstOrDefaultAsync(x => x.Id == order.Id, cancellationToken);
                if (notificationOrder is not null)
                    await notifications.NotifyNewOrderToKitchen(mapper.Map<OrderDto>(notificationOrder));
            }
            catch (Exception notificationError)
            {
                logger.LogWarning(notificationError, "El pedido web {OrderId} se creó, pero la notificación de cocina falló.", order.Id);
            }

            return Ok(ApiResponse<PublicStorefrontOrderResult>.SuccessResponse(ToPublicOrderResult(order, paymentMethod, checkout)));
        }
        catch (StorefrontOrderConflictException ex)
        {
            if (transaction is not null)
                await transaction.RollbackAsync(cancellationToken);
            return Conflict(ApiResponse<PublicStorefrontOrderResult>.ErrorResponse(ex.Message));
        }
        catch (DbUpdateException)
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
            throw;
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }
    }

    [HttpGet("orders/{orderId:int}/payment-status")]
    [EnableRateLimiting("storefront-quote")]
    public async Task<ActionResult<ApiResponse<WompiPaymentStatusResult>>> GetPaymentStatus(
        int orderId,
        [FromServices] StorefrontCustomerAuthService customerAuth,
        CancellationToken cancellationToken)
    {
        var order = await GetVerifiedCustomerOrderAsync(orderId, customerAuth, cancellationToken);
        if (order is null) return NotFound(ApiResponse<WompiPaymentStatusResult>.ErrorResponse("Pedido no encontrado."));
        var result = await wompi.GetOrderPaymentStatusAsync(Math.Max(1, storefrontOptions.Value.TenantId), orderId, cancellationToken);
        return result is null
            ? NotFound(ApiResponse<WompiPaymentStatusResult>.ErrorResponse("Este pedido no tiene un pago en línea."))
            : Ok(ApiResponse<WompiPaymentStatusResult>.SuccessResponse(result));
    }

    [HttpPost("orders/{orderId:int}/payments/wompi/transactions/{transactionId}")]
    [EnableRateLimiting("storefront-quote")]
    public async Task<ActionResult<ApiResponse<WompiPaymentStatusResult>>> RegisterWompiTransaction(
        int orderId,
        string transactionId,
        [FromServices] StorefrontCustomerAuthService customerAuth,
        CancellationToken cancellationToken)
    {
        var order = await GetVerifiedCustomerOrderAsync(orderId, customerAuth, cancellationToken);
        if (order is null) return NotFound(ApiResponse<WompiPaymentStatusResult>.ErrorResponse("Pedido no encontrado."));
        var result = await wompi.SynchronizeTransactionAsync(Math.Max(1, storefrontOptions.Value.TenantId), orderId, transactionId, cancellationToken);
        return result is null
            ? NotFound(ApiResponse<WompiPaymentStatusResult>.ErrorResponse("Este pedido no tiene un pago en línea."))
            : Ok(ApiResponse<WompiPaymentStatusResult>.SuccessResponse(result));
    }

    [HttpPost("orders/{orderId:int}/payments/wompi/retry")]
    [EnableRateLimiting("storefront-quote")]
    public async Task<ActionResult<ApiResponse<WompiCheckoutData>>> RetryWompiPayment(
        int orderId,
        [FromServices] StorefrontCustomerAuthService customerAuth,
        CancellationToken cancellationToken)
    {
        var order = await GetVerifiedCustomerOrderAsync(orderId, customerAuth, cancellationToken);
        if (order is null) return NotFound(ApiResponse<WompiCheckoutData>.ErrorResponse("Pedido no encontrado."));
        var checkout = await wompi.RetryAsync(Math.Max(1, storefrontOptions.Value.TenantId), order, clock.UtcNow, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ApiResponse<WompiCheckoutData>.SuccessResponse(checkout));
    }

    private async Task<Order?> GetVerifiedCustomerOrderAsync(
        int orderId,
        StorefrontCustomerAuthService customerAuth,
        CancellationToken cancellationToken)
    {
        StorefrontCustomerSessionResult session;
        try
        {
            session = await customerAuth.GetSessionAsync(Request.Headers["X-Storefront-Customer-Session"].FirstOrDefault(), cancellationToken);
        }
        catch (StorefrontAuthInvalidSessionException)
        {
            return null;
        }
        return await db.Orders.Include(x => x.Customer).FirstOrDefaultAsync(x =>
            x.Id == orderId
            && x.OrderSource == "web"
            && x.Customer != null
            && (x.Customer.Phone1 == session.Phone || x.Customer.Phone2 == session.Phone), cancellationToken);
    }

    private async Task<PublicStorefrontOrderResult> ToPublicOrderResultAsync(Order order, CancellationToken cancellationToken)
    {
        var payment = await wompi.GetOrderPaymentStatusAsync(Math.Max(1, storefrontOptions.Value.TenantId), order.Id, cancellationToken);
        return ToPublicOrderResult(order, payment is null ? "cash" : "online", payment?.Checkout, payment?.PaymentStatus);
    }

    private static PublicStorefrontOrderResult ToPublicOrderResult(Order order, string paymentMethod, WompiCheckoutData? checkout, string? paymentStatus = null) => new(
        order.Id,
        order.Status.ToString(),
        order.BranchId,
        order.Subtotal,
        order.DeliveryFee ?? 0,
        order.Total,
        paymentMethod,
        paymentStatus ?? (paymentMethod == "cash" ? "NotRequired" : "Pending"),
        checkout);

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
        PublicPromotionDto? promotion)
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
            sb.AppendLine($"*Tiempo estimado:* {PreparationMinutes} min de preparación + {travelMinutes} min de desplazamiento = {PreparationMinutes + travelMinutes} min");
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
        if (fulfillmentType == "delivery")
        {
            sb.AppendLine($"*Domicilio:* {Money(estimatedDeliveryFee)}");
            sb.AppendLine($"*Total estimado:* {Money(subtotal + estimatedDeliveryFee)}");
        }
        else
        {
            sb.AppendLine($"*Total:* {Money(subtotal)}");
        }
        if (promotion is not null)
            sb.AppendLine($"*Promoción vigente:* {promotion.Title} (sujeta a validación final)");
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

public sealed record PublicCatalogDto(
    IReadOnlyCollection<PublicProductGroupDto> RiceGroups,
    IReadOnlyCollection<PublicProductGroupDto> ComboGroups,
    IReadOnlyCollection<PublicProductGroupDto> BeverageGroups,
    IReadOnlyCollection<PublicProductGroupDto> AdditionGroups,
    IReadOnlyCollection<PublicPromotionDto> Promotions,
    IReadOnlyCollection<PublicBranchDto> Branches,
    IReadOnlyCollection<string> Cities,
    int PreparationMinutes,
    int CoverageTravelMinutes,
    int CoverageDistanceMeters);

public sealed record PublicBranchDto(
    int Id,
    string Name,
    string Address,
    decimal Latitude,
    decimal Longitude,
    string ContactWhatsAppUrl,
    IReadOnlyCollection<PublicBranchBusinessHourDto> BusinessHours);
public sealed record PublicBranchBusinessHourDto(
    DayOfWeek DayOfWeek,
    TimeOnly? OpenTime,
    TimeOnly? CloseTime,
    bool IsClosed,
    int DisplayOrder);
public sealed record PublicAddressPreviewDto(
    string FormattedAddress,
    decimal Latitude,
    decimal Longitude,
    bool RequiresConfirmation);

public sealed class PublicAddressPreviewRequest
{
    [Required, StringLength(30)]
    public string City { get; set; } = string.Empty;

    [Required, StringLength(250, MinimumLength = 5)]
    public string Address { get; set; } = string.Empty;

}

public sealed class PublicCoveragePreviewRequest
{
    [Required, StringLength(30)]
    public string City { get; set; } = string.Empty;

    [Required, StringLength(250, MinimumLength = 5)]
    public string Address { get; set; } = string.Empty;

    [Range(-90, 90)]
    public decimal? Latitude { get; set; }

    [Range(-180, 180)]
    public decimal? Longitude { get; set; }
}

public sealed record PublicProductGroupDto(
    string Key,
    int CategoryId,
    string CategoryName,
    string Name,
    string? Description,
    string? Ingredients,
    string? PhotoUrl,
    int SortOrder,
    IReadOnlyCollection<PublicProductOptionDto> Options);

public sealed record PublicProductOptionDto(
    int ProductId,
    string Name,
    string VariantLabel,
    int Price,
    string AvailabilityStatus,
    int? ServesPeopleMin,
    int? ServesPeopleMax);

public sealed record PublicPromotionDto(
    int Id,
    int BranchId,
    string BranchName,
    string Type,
    string Title,
    int? MinimumOrderValue,
    int? GiftProductId,
    string? GiftProductName,
    decimal? DiscountPercentage,
    string? DiscountScope,
    IReadOnlyCollection<int> DiscountProductIds,
    DateTime? EndsAt);

public class PublicDeliveryQuoteRequest
{
    [Required, StringLength(20)]
    public string FulfillmentType { get; set; } = "delivery";

    [Required, StringLength(100, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    private string _phone = string.Empty;

    [Required, StringLength(10, MinimumLength = 10), RegularExpression(@"^3\d{9}$")]
    public string Phone
    {
        get => _phone;
        set => _phone = ColombianMobilePhone.Normalize(value);
    }

    [StringLength(30)]
    public string? City { get; set; }

    [StringLength(250, MinimumLength = 5)]
    public string? Address { get; set; }

    [StringLength(160)]
    public string? AddressAdditionalInfo { get; set; }

    [Range(-90, 90)]
    public decimal? Latitude { get; set; }

    [Range(-180, 180)]
    public decimal? Longitude { get; set; }

    public int? SelectedBranchId { get; set; }

    public int? SavedAddressId { get; set; }

    [StringLength(60)]
    public string? AddressLabel { get; set; }

    public List<PublicCartItemRequest> Items { get; set; } = [];
}

public sealed class PublicStorefrontOrderRequest : PublicDeliveryQuoteRequest
{
    [StringLength(200)]
    public string? OrderNotes { get; set; }
    [RegularExpression("^(cash|online)$")]
    public string PaymentMethod { get; set; } = "cash";
}

public sealed class PublicCartItemRequest
{
    [Range(1, int.MaxValue)]
    public int ProductId { get; set; }

    [Range(1, 50)]
    public int Quantity { get; set; }

    [StringLength(200)]
    public string? Notes { get; set; }
}

public sealed record PublicBranchQuoteDto(
    int Id,
    string Name,
    string Address,
    decimal Latitude,
    decimal Longitude,
    int DistanceMeters,
    int EstimatedDeliveryFee,
    int EstimatedTotalMinutes,
    int TravelMinutes,
    bool IsWithinCoverage,
    bool IsRecommended,
    bool IsSelected,
    string? RoutePolyline = null);

public sealed record PublicCoveragePreviewDto(
    string FormattedAddress,
    decimal Latitude,
    decimal Longitude,
    IReadOnlyCollection<PublicBranchQuoteDto> Branches,
    int RecommendedBranchId,
    int CoverageDistanceMeters,
    int CoverageTravelMinutes);

public sealed record PublicCartLineDto(
    int ProductId,
    string Name,
    int Quantity,
    int UnitPrice,
    int Subtotal,
    string? Notes);

public sealed record PublicDeliveryQuoteDto(
    string FulfillmentType,
    string? FormattedAddress,
    decimal? Latitude,
    decimal? Longitude,
    IReadOnlyCollection<PublicBranchQuoteDto> Branches,
    int RecommendedBranchId,
    int SelectedBranchId,
    int CheckoutBranchId,
    int DistanceMeters,
    int EstimatedDeliveryFee,
    int TravelMinutes,
    int PreparationMinutes,
    int EstimatedTotalMinutes,
    bool IsOutsideCoverage,
    IReadOnlyCollection<PublicCartLineDto> Items,
    int Subtotal,
    int Total,
    PublicPromotionDto? Promotion,
    string WhatsAppUrl,
    string DeliveryFeeSource = "estimated",
    bool OnlinePaymentAvailable = false);

public sealed record PublicStorefrontOrderResult(
    int OrderId,
    string Status,
    int BranchId,
    int Subtotal,
    int DeliveryFee,
    int Total,
    string PaymentMethod,
    string PaymentStatus,
    WompiCheckoutData? WompiCheckout);

public sealed class StorefrontOrderConflictException(string message) : Exception(message);

internal static class ColombianMobilePhone
{
    public static string Normalize(string? value)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        return digits.Length > 10 ? digits[^10..] : digits;
    }

    public static bool IsValid(string? value)
    {
        var normalized = Normalize(value);
        return normalized.Length == 10 && normalized[0] == '3';
    }
}
