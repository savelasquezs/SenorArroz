using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Common.Interfaces;

public interface IIntegrationSecretProtector
{
    string Protect(string plainText);
    string Unprotect(string protectedText);
}

public interface IWompiPaymentService
{
    Task<WompiPaymentIntegration?> GetEnabledIntegrationAsync(int tenantId, int branchId, CancellationToken cancellationToken);
    WompiCheckoutData CreateAttempt(Order order, WompiPaymentIntegration integration, DateTime utcNow);
    WompiCheckoutData CreateCheckoutAttempt(StorefrontCheckout checkout, WompiPaymentIntegration integration, DateTime utcNow);
    Task<WompiWebhookProcessingResult> ProcessWebhookAsync(string environment, string rawPayload, string? headerChecksum, CancellationToken cancellationToken);
    Task<WompiPaymentStatusResult?> GetOrderPaymentStatusAsync(int tenantId, int orderId, CancellationToken cancellationToken);
    Task<WompiPaymentStatusResult?> SynchronizeTransactionAsync(int tenantId, int orderId, string providerTransactionId, CancellationToken cancellationToken);
    Task<WompiCheckoutData> RetryAsync(int tenantId, Order order, DateTime utcNow, CancellationToken cancellationToken);
    Task<WompiStorefrontCheckoutStatusResult?> GetCheckoutPaymentStatusAsync(int tenantId, string checkoutPublicId, CancellationToken cancellationToken);
    Task<WompiStorefrontCheckoutStatusResult?> SynchronizeCheckoutTransactionAsync(int tenantId, string checkoutPublicId, string providerTransactionId, CancellationToken cancellationToken);
    Task<WompiCheckoutData> RetryCheckoutAsync(int tenantId, StorefrontCheckout checkout, DateTime utcNow, CancellationToken cancellationToken);
    Task<WompiManualReviewResult> ResolveManualReviewAsync(int attemptId, int reviewedByUserId, bool approve, DateTime utcNow, CancellationToken cancellationToken);
    Task<bool> TestPublicKeyAsync(string environment, string publicKey, CancellationToken cancellationToken);
}

public interface IPaymentReviewNotificationService
{
    Task NotifyReviewRequiredAsync(int branchId, int orderId, int paymentAttemptId, string reason, CancellationToken cancellationToken);
}

public record WompiCheckoutData(
    string PublicKey,
    string Currency,
    long AmountInCents,
    string Reference,
    string IntegritySignature,
    string ExpiresAt,
    string Environment);

public record WompiPaymentStatusResult(
    int OrderId,
    string OrderStatus,
    string PaymentStatus,
    bool RequiresManualReview,
    string? ManualReviewReason,
    string? ProviderTransactionId,
    WompiCheckoutData? Checkout);

public record WompiStorefrontCheckoutStatusResult(
    string CheckoutId,
    int? OrderId,
    string CheckoutStatus,
    string PaymentStatus,
    bool RequiresManualReview,
    string? ManualReviewReason,
    string? ProviderTransactionId,
    WompiCheckoutData? Checkout);

public record WompiWebhookProcessingResult(bool Accepted, bool Duplicate, bool RequiresManualReview, int? BranchId, int? OrderId, int? PaymentAttemptId, string? Error = null);
public record WompiManualReviewResult(int OrderId, int BranchId, string PaymentStatus, string OrderStatus);

public interface IRappiDeliveryProvider
{
    bool CredentialsConfigured { get; }
    Task<RappiConnectionResult> TestConnectionAsync(CancellationToken cancellationToken);
    Task<RappiOperationResult> SetStoreIntegratedAsync(
        string storeId,
        bool integrated,
        CancellationToken cancellationToken);
    Task<RappiWebhookResult> ConfigureWebhookAsync(
        string eventType,
        string webhookUrl,
        IReadOnlyCollection<string> storeIds,
        CancellationToken cancellationToken);
    Task<RappiWebhookConfigurationResult> GetWebhookAsync(
        string eventType,
        CancellationToken cancellationToken);
    Task<RappiWebhookResult> ResetWebhookSecretAsync(
        string eventType,
        CancellationToken cancellationToken);
    Task<RappiOperationResult> PublishMenuAsync(RappiMenuRequest menu, CancellationToken cancellationToken);
    Task<RappiOperationResult> GetMenuApprovalAsync(string storeId, CancellationToken cancellationToken);
    Task<RappiOperationResult> SetAvailabilityAsync(
        IReadOnlyCollection<RappiAvailabilityRequest> stores,
        CancellationToken cancellationToken);
    Task<RappiOrdersResult> GetSentOrdersAsync(CancellationToken cancellationToken);
    Task<RappiOperationResult> AcceptOrderAsync(string orderId, int cookingTimeMinutes, CancellationToken cancellationToken);
    Task<RappiOperationResult> RejectOrderAsync(string orderId, string reason, CancellationToken cancellationToken);
    Task<RappiOperationResult> ReadyForPickupAsync(string orderId, CancellationToken cancellationToken);
    Task<RappiOrderEventsResult> GetOrderEventsAsync(string orderId, CancellationToken cancellationToken);
}

public interface IExternalDeliveryStatusSyncService
{
    Task SyncReadyForPickupAsync(int internalOrderId, CancellationToken cancellationToken);
}

public interface IRappiOrderProcessor
{
    Task<RappiOrderProcessingResult> IngestNewOrderAsync(
        int connectionId,
        string rawOrderJson,
        CancellationToken cancellationToken);
    Task<RappiOrderProcessingResult> RevalidateAndAcceptAsync(
        int externalOrderId,
        int? actorUserId,
        CancellationToken cancellationToken);
    Task<RappiOperationResult> RejectAsync(
        int externalOrderId,
        string reason,
        CancellationToken cancellationToken);
    Task ProcessPendingWebhookEventsAsync(CancellationToken cancellationToken);
}

public record RappiConnectionResult(
    bool Success,
    IReadOnlyList<RappiStoreInfo>? Stores = null,
    string? Error = null);

public record RappiStoreInfo(string StoreId, string? IntegrationId, string Name);
public record RappiWebhookResult(bool Success, string? Secret = null, string? Error = null);
public record RappiWebhookConfigurationResult(
    bool Success,
    IReadOnlyList<string>? EnabledStoreIds = null,
    string? Error = null);
public record RappiOperationResult(bool Success, int? StatusCode = null, string? Error = null);
public record RappiOrdersResult(bool Success, IReadOnlyList<string>? RawOrders = null, string? Error = null);
public record RappiOrderEventsResult(bool Success, IReadOnlyList<string>? Events = null, string? Error = null);
public record RappiOrderProcessingResult(
    bool Success,
    int? ExternalOrderId = null,
    int? InternalOrderId = null,
    bool Held = false,
    string? Error = null);

public record RappiMenuRequest(string StoreId, IReadOnlyList<RappiMenuItem> Items);

public record RappiMenuItem(
    RappiMenuCategory Category,
    IReadOnlyList<object> Children,
    string Name,
    string? Description,
    int Price,
    string Sku,
    int SortingPosition,
    string Type,
    string? ImageUrl);

public record RappiMenuCategory(
    string Id,
    int MaxQty,
    int MinQty,
    string Name,
    int SortingPosition);

public record RappiAvailabilityRequest(
    string StoreIntegrationId,
    IReadOnlyList<string> TurnOn,
    IReadOnlyList<string> TurnOff);
