using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SenorArroz.API.Security;
using SenorArroz.Application.Common.Interfaces;
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
    IMemoryCache cache,
    StorefrontQuoteConcurrencyGate concurrencyGate) : ControllerBase
{
    private const int PreparationMinutes = 20;
    private const int CoverageTravelMinutes = 30;
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

        var branches = await EligibleBranchesQuery()
            .OrderBy(x => x.Name)
            .Select(x => new PublicBranchDto(x.Id, x.Name, x.Address))
            .ToListAsync(cancellationToken);

        var result = new PublicCatalogDto(
            BuildGroups(products, "rice"),
            BuildGroups(products, "combo"),
            BuildGroups(products, "beverage"),
            BuildGroups(products, "addition"),
            [],
            branches,
            AllowedCities,
            PreparationMinutes,
            CoverageTravelMinutes);

        return Ok(ApiResponse<PublicCatalogDto>.SuccessResponse(result));
    }

    [HttpPost("delivery-quote")]
    [RequestSizeLimit(32 * 1024)]
    [EnableRateLimiting("storefront-quote")]
    public async Task<ActionResult<ApiResponse<PublicDeliveryQuoteDto>>> Quote(
        [FromBody] PublicDeliveryQuoteRequest request,
        CancellationToken cancellationToken)
    {
        using var concurrencyLease = await concurrencyGate.TryEnter(cancellationToken);
        if (concurrencyLease is null)
            return StatusCode(StatusCodes.Status429TooManyRequests,
                ApiResponse<PublicDeliveryQuoteDto>.ErrorResponse("Hay muchas cotizaciones en curso. Intenta nuevamente en unos segundos."));

        if (!request.AcceptDataProcessing)
            return BadRequest(ApiResponse<PublicDeliveryQuoteDto>.ErrorResponse("Debes autorizar el tratamiento de datos para gestionar tu solicitud."));

        var normalizedCity = MatchAllowedCity(request.City);
        if (normalizedCity is null)
            return BadRequest(ApiResponse<PublicDeliveryQuoteDto>.ErrorResponse("La ciudad debe ser Medellín, Bello o Copacabana."));

        var digits = DigitsOnly(request.Phone);
        if (digits.Length is < 10 or > 15)
            return BadRequest(ApiResponse<PublicDeliveryQuoteDto>.ErrorResponse("Ingresa un teléfono válido."));

        var resolved = await ResolveAddressCached(
            request.Address,
            request.Latitude,
            request.Longitude,
            cancellationToken);
        if (resolved.Result is null)
            return BadRequest(ApiResponse<PublicDeliveryQuoteDto>.ErrorResponse(resolved.Error ?? "No fue posible validar la ubicación."));

        var confirmedByMap = request.Latitude.HasValue && request.Longitude.HasValue;
        if (!confirmedByMap && resolved.Result.RequiresConfirmation)
            return BadRequest(ApiResponse<PublicDeliveryQuoteDto>.ErrorResponse("Selecciona y confirma una dirección exacta en Google Maps."));

        if (!AddressMatchesCity(resolved.Result.FormattedAddress, normalizedCity))
            return BadRequest(ApiResponse<PublicDeliveryQuoteDto>.ErrorResponse($"La ubicación confirmada no pertenece a {normalizedCity}."));

        var branchRows = await EligibleBranchesQuery()
            .Select(x => new BranchRouteSource(
                x.Id,
                x.Name,
                x.Address,
                x.Latitude!.Value,
                x.Longitude!.Value,
                x.WhatsAppSetting!.DisplayPhoneNumber))
            .ToListAsync(cancellationToken);
        if (branchRows.Count == 0)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiResponse<PublicDeliveryQuoteDto>.ErrorResponse("No hay sucursales habilitadas para pedidos web."));

        var productIds = request.Items.Select(x => x.ProductId).Distinct().ToList();
        var products = await db.Products
            .AsNoTracking()
            .Include(x => x.Category)
            .Where(x => productIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var validationError = ValidateItems(request.Items, products);
        if (validationError is not null)
            return BadRequest(ApiResponse<PublicDeliveryQuoteDto>.ErrorResponse(validationError));

        var destination = ((double)resolved.Result.Latitude, (double)resolved.Result.Longitude);
        var routeTasks = branchRows.Select(async branch =>
        {
            var metrics = await RouteCached(branch, destination, cancellationToken);
            return new { Branch = branch, Metrics = metrics };
        });
        var routeResults = await Task.WhenAll(routeTasks);
        var validRoutes = routeResults
            .Where(x => x.Metrics.DurationSeconds > 0)
            .OrderBy(x => x.Metrics.DurationSeconds)
            .ToList();
        if (validRoutes.Count == 0)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiResponse<PublicDeliveryQuoteDto>.ErrorResponse("Google Maps no pudo calcular el desplazamiento en este momento."));

        var nearest = validRoutes[0];
        var selected = validRoutes.FirstOrDefault(x => x.Branch.Id == request.SelectedBranchId) ?? nearest;
        var selectedTravelMinutes = ToMinutes(selected.Metrics.DurationSeconds);
        var outsideCoverage = selectedTravelMinutes > CoverageTravelMinutes;
        var checkout = outsideCoverage ? nearest : selected;
        var checkoutTravelMinutes = ToMinutes(checkout.Metrics.DurationSeconds);
        var cartLines = request.Items.Select(item =>
        {
            var product = products[item.ProductId];
            return new PublicCartLineDto(product.Id, product.Name, item.Quantity, product.Price, product.Price * item.Quantity, item.Notes?.Trim());
        }).ToList();
        var subtotal = cartLines.Sum(x => x.Subtotal);
        var promotion = await GetActivePromotion(checkout.Branch.Id, cancellationToken);
        var promotionDto = promotion is null ? null : ToPromotionDto(promotion);
        var message = BuildWhatsAppMessage(
            request,
            normalizedCity,
            resolved.Result.FormattedAddress,
            resolved.Result.Latitude,
            resolved.Result.Longitude,
            cartLines,
            subtotal,
            checkout.Branch.Name,
            checkoutTravelMinutes,
            outsideCoverage,
            promotionDto);
        if (message.Length > MaxWhatsAppMessageLength)
            return BadRequest(ApiResponse<PublicDeliveryQuoteDto>.ErrorResponse("El pedido contiene demasiada información. Reduce las notas e intenta nuevamente."));
        var whatsappUrl = $"https://wa.me/{DigitsOnly(checkout.Branch.WhatsAppPhone)}?text={Uri.EscapeDataString(message)}";

        var branchOptions = validRoutes.Select(x =>
        {
            var travel = ToMinutes(x.Metrics.DurationSeconds);
            return new PublicBranchQuoteDto(
                x.Branch.Id,
                x.Branch.Name,
                x.Branch.Address,
                travel,
                PreparationMinutes + travel,
                travel <= CoverageTravelMinutes,
                x.Branch.Id == nearest.Branch.Id,
                x.Branch.Id == selected.Branch.Id);
        }).ToList();

        var result = new PublicDeliveryQuoteDto(
            resolved.Result.FormattedAddress,
            resolved.Result.Latitude,
            resolved.Result.Longitude,
            branchOptions,
            nearest.Branch.Id,
            selected.Branch.Id,
            checkout.Branch.Id,
            selectedTravelMinutes,
            PreparationMinutes,
            PreparationMinutes + selectedTravelMinutes,
            outsideCoverage,
            cartLines,
            subtotal,
            promotionDto,
            whatsappUrl);

        return Ok(ApiResponse<PublicDeliveryQuoteDto>.SuccessResponse(result));
    }

    private IQueryable<Branch> EligibleBranchesQuery() => db.Branches
        .AsNoTracking()
        .Where(x => x.IsActive
            && x.Latitude.HasValue
            && x.Longitude.HasValue
            && x.WhatsAppSetting != null
            && x.WhatsAppSetting.IsActive
            && x.WhatsAppSetting.IsVerified
            && x.WhatsAppSetting.DisplayPhoneNumber != "");

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
                .OrderBy(x => x.StorefrontSortOrder)
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
        string city,
        string formattedAddress,
        decimal latitude,
        decimal longitude,
        IReadOnlyCollection<PublicCartLineDto> lines,
        int subtotal,
        string branchName,
        int travelMinutes,
        bool outsideCoverage,
        PublicPromotionDto? promotion)
    {
        var sb = new StringBuilder();
        sb.AppendLine(outsideCoverage ? "*SOLICITUD FUERA DE COBERTURA*" : "*NUEVO PEDIDO WEB*");
        sb.AppendLine();
        sb.AppendLine($"*Cliente:* {SingleLine(request.Name)}");
        sb.AppendLine($"*Teléfono:* {DigitsOnly(request.Phone)}");
        sb.AppendLine($"*Ciudad:* {city}");
        sb.AppendLine($"*Dirección confirmada:* {formattedAddress}");
        sb.AppendLine($"*Ubicación:* https://www.google.com/maps?q={latitude.ToString(CultureInfo.InvariantCulture)},{longitude.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"*Sucursal sugerida:* {branchName}");
        sb.AppendLine($"*Tiempo estimado:* {PreparationMinutes} min de preparación + {travelMinutes} min de desplazamiento = {PreparationMinutes + travelMinutes} min");
        sb.AppendLine();
        sb.AppendLine("*Pedido:*");
        foreach (var line in lines)
        {
            sb.AppendLine($"• {line.Quantity} × {line.Name} — {Money(line.Subtotal)}");
            if (!string.IsNullOrWhiteSpace(line.Notes))
                sb.AppendLine($"  Nota: {SingleLine(line.Notes)}");
        }
        sb.AppendLine($"*Subtotal:* {Money(subtotal)}");
        if (promotion is not null)
            sb.AppendLine($"*Promoción vigente:* {promotion.Title} (sujeta a validación final)");
        if (outsideCoverage)
        {
            sb.AppendLine();
            sb.AppendLine("La ubicación supera 30 minutos de desplazamiento. El cliente fue informado de que el envío está sujeto a autorización de la sucursal.");
        }
        sb.AppendLine();
        sb.AppendLine("*Autorización de datos:* aceptada (Política de privacidad v2026-08-24)");
        sb.AppendLine($"*Autorización de promociones:* {(request.AcceptMarketing ? "aceptada" : "no aceptada")}");
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

    private static string DigitsOnly(string? value) => new((value ?? string.Empty).Where(char.IsDigit).ToArray());
    private static int ToMinutes(int durationSeconds) => (int)Math.Ceiling(durationSeconds / 60m);
    private static string Money(int value) => value.ToString("C0", CultureInfo.GetCultureInfo("es-CO"));
    private static string SingleLine(string? value) => string.Join(' ', (value ?? string.Empty)
        .Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    private sealed record BranchRouteSource(
        int Id,
        string Name,
        string Address,
        decimal Latitude,
        decimal Longitude,
        string WhatsAppPhone);
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
    int CoverageTravelMinutes);

public sealed record PublicBranchDto(int Id, string Name, string Address);
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

public sealed class PublicDeliveryQuoteRequest
{
    [Required, StringLength(100, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(20, MinimumLength = 10)]
    public string Phone { get; set; } = string.Empty;

    [Required, StringLength(30)]
    public string City { get; set; } = string.Empty;

    [Required, StringLength(250, MinimumLength = 5)]
    public string Address { get; set; } = string.Empty;

    [Range(-90, 90)]
    public decimal? Latitude { get; set; }

    [Range(-180, 180)]
    public decimal? Longitude { get; set; }

    public int? SelectedBranchId { get; set; }

    [Range(typeof(bool), "true", "true", ErrorMessage = "Debes autorizar el tratamiento de datos.")]
    public bool AcceptDataProcessing { get; set; }

    public bool AcceptMarketing { get; set; }
    public List<PublicCartItemRequest> Items { get; set; } = [];
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
    int TravelMinutes,
    int EstimatedTotalMinutes,
    bool IsWithinCoverage,
    bool IsRecommended,
    bool IsSelected);

public sealed record PublicCartLineDto(
    int ProductId,
    string Name,
    int Quantity,
    int UnitPrice,
    int Subtotal,
    string? Notes);

public sealed record PublicDeliveryQuoteDto(
    string FormattedAddress,
    decimal Latitude,
    decimal Longitude,
    IReadOnlyCollection<PublicBranchQuoteDto> Branches,
    int RecommendedBranchId,
    int SelectedBranchId,
    int CheckoutBranchId,
    int TravelMinutes,
    int PreparationMinutes,
    int EstimatedTotalMinutes,
    bool IsOutsideCoverage,
    IReadOnlyCollection<PublicCartLineDto> Items,
    int Subtotal,
    PublicPromotionDto? Promotion,
    string WhatsAppUrl);
