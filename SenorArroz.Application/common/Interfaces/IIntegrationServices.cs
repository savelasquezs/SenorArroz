using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Common.Interfaces;

public interface IIntegrationSecretProtector
{
    string Protect(string plainText);
    string Unprotect(string protectedText);
}

public interface IRappiDeliveryProvider
{
    Task<RappiConnectionResult> TestConnectionAsync(DeliveryAppConnection connection, string clientSecret, CancellationToken cancellationToken);
    Task<RappiWebhookResult> ConfigureWebhookAsync(DeliveryAppConnection connection, string clientSecret, string webhookUrl, CancellationToken cancellationToken);
    Task<IReadOnlyList<RappiCatalogItem>> GetCatalogAsync(DeliveryAppConnection connection, string clientSecret, CancellationToken cancellationToken);
    Task<RappiOperationResult> AcceptOrderAsync(DeliveryAppConnection connection, string clientSecret, string orderId, int cookingTimeMinutes, CancellationToken cancellationToken);
    Task<RappiOperationResult> RejectOrderAsync(DeliveryAppConnection connection, string clientSecret, string orderId, CancellationToken cancellationToken);
    Task<RappiOperationResult> ReadyForPickupAsync(DeliveryAppConnection connection, string clientSecret, string orderId, CancellationToken cancellationToken);
}

public interface IExternalDeliveryStatusSyncService
{
    Task SyncReadyForPickupAsync(int internalOrderId, CancellationToken cancellationToken);
}

public record RappiConnectionResult(bool Success, string? Error = null);
public record RappiWebhookResult(bool Success, string? Secret = null, string? Error = null);
public record RappiOperationResult(bool Success, string? Error = null);
public record RappiCatalogItem(string ExternalProductId, string Sku, string Name, string ItemType, bool IsActive = true);
