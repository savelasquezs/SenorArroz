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

namespace SenorArroz.API.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = StorefrontApiKeyOptions.Scheme)]
[Route("api/public/storefront")]
public sealed class PublicStorefrontController(
    IApplicationDbContext db,
    IClock clock,
    IWompiPaymentService wompi,
    IOptions<StorefrontCustomerAuthOptions> storefrontOptions,
    StorefrontCommerceService commerce) : ControllerBase
{
    private readonly StorefrontCommerceService _commerce = commerce;

    [HttpGet("catalog")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [EnableRateLimiting("storefront-catalog")]
    public Task<ActionResult<ApiResponse<PublicCatalogDto>>> GetCatalog(CancellationToken cancellationToken)
        => _commerce.GetCatalog(cancellationToken);

    [HttpGet("branches/availability")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [EnableRateLimiting("storefront-catalog")]
    public Task<ActionResult<ApiResponse<IReadOnlyCollection<PublicBranchAvailabilityDto>>>> GetBranchAvailability(CancellationToken cancellationToken)
        => _commerce.GetBranchAvailability(cancellationToken);

    [HttpPost("address-preview")]
    [RequestSizeLimit(4 * 1024)]
    [EnableRateLimiting("storefront-quote")]
    public Task<ActionResult<ApiResponse<PublicAddressPreviewDto>>> PreviewAddress([FromBody] PublicAddressPreviewRequest request, CancellationToken cancellationToken)
        => _commerce.PreviewAddress(request, cancellationToken);

    [HttpPost("coverage-preview")]
    [RequestSizeLimit(8 * 1024)]
    [EnableRateLimiting("storefront-quote")]
    public Task<ActionResult<ApiResponse<PublicCoveragePreviewDto>>> PreviewCoverage([FromBody] PublicCoveragePreviewRequest request, CancellationToken cancellationToken)
        => _commerce.PreviewCoverage(request, cancellationToken);

    [HttpPost("delivery-quote")]
    [RequestSizeLimit(32 * 1024)]
    [EnableRateLimiting("storefront-quote")]
    public Task<ActionResult<ApiResponse<PublicDeliveryQuoteDto>>> Quote(
        [FromBody] PublicDeliveryQuoteRequest request,
        CancellationToken cancellationToken,
        [FromServices] StorefrontCustomerAuthService? customerAuth = null)
        => _commerce.QuoteCore(request, cancellationToken, customerAuth, null,
            customerAuth is null ? null : Request.Headers["X-Storefront-Customer-Session"].FirstOrDefault());

    [HttpPost("orders")]
    [RequestSizeLimit(32 * 1024)]
    [EnableRateLimiting("storefront-quote")]
    public Task<ActionResult<ApiResponse<PublicStorefrontOrderResult>>> ConfirmOrder(
        [FromBody] PublicStorefrontOrderRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        [FromServices] StorefrontCustomerAuthService customerAuth,
        [FromServices] IMapper mapper,
        [FromServices] IOrderNotificationService notifications,
        [FromServices] ILogger<PublicStorefrontController> logger,
        CancellationToken cancellationToken)
        => _commerce.ConfirmOrderCore(request, idempotencyKey, customerAuth, null, mapper, notifications, logger, "web", null, cancellationToken, Request.Headers["X-Storefront-Customer-Session"].FirstOrDefault());

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

    [HttpGet("checkouts/{checkoutId}/payment-status")]
    [EnableRateLimiting("storefront-quote")]
    public async Task<ActionResult<ApiResponse<WompiStorefrontCheckoutStatusResult>>> GetCheckoutPaymentStatus(
        string checkoutId,
        [FromServices] StorefrontCustomerAuthService customerAuth,
        CancellationToken cancellationToken)
    {
        var checkout = await GetVerifiedCustomerCheckoutAsync(checkoutId, customerAuth, cancellationToken);
        if (checkout is null) return NotFound(ApiResponse<WompiStorefrontCheckoutStatusResult>.ErrorResponse("Checkout no encontrado."));
        var result = await wompi.GetCheckoutPaymentStatusAsync(Math.Max(1, storefrontOptions.Value.TenantId), checkoutId, cancellationToken);
        return result is null
            ? NotFound(ApiResponse<WompiStorefrontCheckoutStatusResult>.ErrorResponse("Este checkout no tiene un pago en línea."))
            : Ok(ApiResponse<WompiStorefrontCheckoutStatusResult>.SuccessResponse(result));
    }

    [HttpPost("checkouts/{checkoutId}/payments/wompi/transactions/{transactionId}")]
    [EnableRateLimiting("storefront-quote")]
    public async Task<ActionResult<ApiResponse<WompiStorefrontCheckoutStatusResult>>> RegisterCheckoutTransaction(
        string checkoutId,
        string transactionId,
        [FromServices] StorefrontCustomerAuthService customerAuth,
        CancellationToken cancellationToken)
    {
        var checkout = await GetVerifiedCustomerCheckoutAsync(checkoutId, customerAuth, cancellationToken);
        if (checkout is null) return NotFound(ApiResponse<WompiStorefrontCheckoutStatusResult>.ErrorResponse("Checkout no encontrado."));
        var result = await wompi.SynchronizeCheckoutTransactionAsync(
            Math.Max(1, storefrontOptions.Value.TenantId), checkoutId, transactionId, cancellationToken);
        return result is null
            ? NotFound(ApiResponse<WompiStorefrontCheckoutStatusResult>.ErrorResponse("Este checkout no tiene un pago en línea."))
            : Ok(ApiResponse<WompiStorefrontCheckoutStatusResult>.SuccessResponse(result));
    }

    [HttpPost("checkouts/{checkoutId}/payments/wompi/retry")]
    [EnableRateLimiting("storefront-quote")]
    public async Task<ActionResult<ApiResponse<WompiCheckoutData>>> RetryCheckoutPayment(
        string checkoutId,
        [FromServices] StorefrontCustomerAuthService customerAuth,
        CancellationToken cancellationToken)
    {
        var checkout = await GetVerifiedCustomerCheckoutAsync(checkoutId, customerAuth, cancellationToken);
        if (checkout is null) return NotFound(ApiResponse<WompiCheckoutData>.ErrorResponse("Checkout no encontrado."));
        var result = await wompi.RetryCheckoutAsync(
            Math.Max(1, storefrontOptions.Value.TenantId), checkout, clock.UtcNow, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ApiResponse<WompiCheckoutData>.SuccessResponse(result));
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

    private async Task<StorefrontCheckout?> GetVerifiedCustomerCheckoutAsync(
        string checkoutId,
        StorefrontCustomerAuthService customerAuth,
        CancellationToken cancellationToken)
    {
        try
        {
            var session = await customerAuth.GetSessionAsync(Request.Headers["X-Storefront-Customer-Session"].FirstOrDefault(), cancellationToken);
            return await db.StorefrontCheckouts.FirstOrDefaultAsync(
                x => x.PublicId == checkoutId && x.CustomerPhone == session.Phone,
                cancellationToken);
        }
        catch (StorefrontAuthInvalidSessionException)
        {
            return null;
        }
    }

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
public sealed record PublicBranchAvailabilityDto(
    int BranchId,
    bool IsConfigured,
    bool IsOpen,
    bool CanReceiveOrders,
    bool IsAvailable,
    DateTime? NextOpeningAtUtc,
    DateTime? ClosingAtUtc,
    bool IsClosingSoon);
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

public sealed record PublicBenefitDto(
    string Source,
    int SourceId,
    string Title,
    string RewardType,
    int? GiftProductId,
    string? GiftProductName,
    decimal? DiscountPercentage,
    string? DiscountScope,
    IReadOnlyCollection<int> DiscountProductIds,
    string Snapshot);

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

    [RegularExpression("^(daily_promotion|loyalty)?$")]
    public string? BenefitSelection { get; set; }

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
    string? Notes,
    int Discount = 0,
    bool IsGift = false);

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
    int DeliveryPromiseMinMinutes,
    int DeliveryPromiseMaxMinutes,
    bool IsOutsideCoverage,
    IReadOnlyCollection<PublicCartLineDto> Items,
    int Subtotal,
    int DiscountTotal,
    int Total,
    PublicPromotionDto? Promotion,
    IReadOnlyCollection<PublicBenefitDto> AvailableBenefits,
    PublicBenefitDto? AppliedBenefit,
    bool BenefitConflict,
    string WhatsAppUrl,
    string DeliveryFeeSource = "estimated",
    bool OnlinePaymentAvailable = false);

public sealed record PublicStorefrontOrderResult(
    int? OrderId,
    string? CheckoutId,
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
