using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Common.Interfaces;

public interface IIntegrationSecretProtector
{
    string Protect(string plainText);
    string Unprotect(string protectedText);
}

public interface IRappiDeliveryProvider
{
    bool CredentialsConfigured { get; }
    Task<RappiConnectionResult> TestConnectionAsync(CancellationToken cancellationToken);
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
    Task<bool> SyncCancellationAsync(
        int internalOrderId,
        string reason,
        CancellationToken cancellationToken);
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
