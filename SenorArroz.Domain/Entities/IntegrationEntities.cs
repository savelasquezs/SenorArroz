using SenorArroz.Domain.Entities.Common;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Domain.Entities;

public class DeliveryAppConnection : BaseEntity
{
    public int BranchId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Environment { get; set; } = "sandbox";
    public string DisplayName { get; set; } = string.Empty;
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public int FinancialAppId { get; set; }
    public int? CustomerId { get; set; }
    public int? TechnicalUserId { get; set; }
    public int DefaultCookingTimeMinutes { get; set; } = 30;
    public decimal EstimatedCommissionRate { get; set; } = 0.25m;
    public int PiiRetentionDays { get; set; } = 90;
    public bool WebhookConfigured { get; set; }
    public bool IsActive { get; set; }
    public bool IsVerified { get; set; }
    public DateTime? LastVerifiedAt { get; set; }
    public DateTime? LastMenuPublishedAt { get; set; }
    public DateTime? LastAvailabilitySyncAt { get; set; }
    public DateTime? LastWebhookAt { get; set; }
    public string? LastError { get; set; }

    public virtual Branch Branch { get; set; } = null!;
    public virtual App FinancialApp { get; set; } = null!;
    public virtual Customer? Customer { get; set; }
    public virtual User? TechnicalUser { get; set; }
    public virtual ICollection<DeliveryAppStore> Stores { get; set; } = [];
    public virtual ICollection<DeliveryAppWebhookSubscription> WebhookSubscriptions { get; set; } = [];
    public virtual ICollection<DeliveryAppProductMapping> ProductMappings { get; set; } = [];
    public virtual ICollection<ExternalDeliveryOrder> ExternalOrders { get; set; } = [];
    public virtual ICollection<RappiMenuPublication> MenuPublications { get; set; } = [];
}

public class DeliveryAppStore : BaseEntity
{
    public int ConnectionId { get; set; }
    public string RappiStoreId { get; set; } = string.Empty;
    public string? StoreIntegrationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsParent { get; set; }
    public bool ManualReadyForPickupEnabled { get; set; }
    public bool? ConnectivityEnabled { get; set; }
    public DateTime? LastPingAt { get; set; }
    public DateTime? LastConnectivityAt { get; set; }
    public string? LastError { get; set; }

    public virtual DeliveryAppConnection Connection { get; set; } = null!;
}

public class DeliveryAppWebhookSubscription : BaseEntity
{
    public int ConnectionId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string EncryptedSecret { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime? LastReceivedAt { get; set; }
    public string? LastError { get; set; }

    public virtual DeliveryAppConnection Connection { get; set; } = null!;
}

public class DeliveryAppProductMapping : BaseEntity
{
    public int ConnectionId { get; set; }
    public int ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string CategorySku { get; set; } = string.Empty;
    public bool IsSelected { get; set; } = true;
    public string? OverrideName { get; set; }
    public string? OverrideDescription { get; set; }
    public string? OverrideImageUrl { get; set; }
    public int? OverridePrice { get; set; }
    public string? PublishedName { get; set; }
    public string? PublishedDescription { get; set; }
    public string? PublishedImageUrl { get; set; }
    public int? PublishedPrice { get; set; }
    public DateTime? PublishedAt { get; set; }

    public virtual DeliveryAppConnection Connection { get; set; } = null!;
    public virtual Product Product { get; set; } = null!;
}

public class ExternalDeliveryOrder : BaseEntity
{
    public int ConnectionId { get; set; }
    public int? StoreId { get; set; }
    public int BranchId { get; set; }
    public string ExternalOrderId { get; set; } = string.Empty;
    public string ExternalStoreId { get; set; } = string.Empty;
    public ExternalOrderStatus Status { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }
    public string? DeliveryAddress { get; set; }
    public string DeliveryMethod { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public int Total { get; set; }
    public int TotalProducts { get; set; }
    public int TotalDiscounts { get; set; }
    public int TotalDiscountByPartner { get; set; }
    public int TotalDiscountByRappi { get; set; }
    public int TotalCharges { get; set; }
    public int CookingTimeMinutes { get; set; }
    public string RawPayloadJson { get; set; } = "{}";
    public string LinesJson { get; set; } = "[]";
    public string DiscountsJson { get; set; } = "[]";
    public string? ValidationErrorsJson { get; set; }
    public int? InternalOrderId { get; set; }
    public int? AcceptedByUserId { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public DateTime? PiiPurgedAt { get; set; }
    public string? LastError { get; set; }

    public virtual DeliveryAppConnection Connection { get; set; } = null!;
    public virtual DeliveryAppStore? Store { get; set; }
    public virtual Order? InternalOrder { get; set; }
}

public class IntegrationWebhookEvent : BaseEntity
{
    public int ConnectionId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string EventKey { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public string Status { get; set; } = "received";
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
    public DateTime? ProcessedAt { get; set; }
}

public class RappiMenuPublication : BaseEntity
{
    public int ConnectionId { get; set; }
    public string StoreId { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public string Status { get; set; } = "pending";
    public string? Error { get; set; }
    public DateTime? CompletedAt { get; set; }

    public virtual DeliveryAppConnection Connection { get; set; } = null!;
}

public class RappiAvailabilityState : BaseEntity
{
    public int ConnectionId { get; set; }
    public int StoreId { get; set; }
    public int ProductMappingId { get; set; }
    public bool DesiredAvailable { get; set; }
    public bool? LastSyncedAvailable { get; set; }
    public string Status { get; set; } = "pending";
    public int AttemptCount { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public string? LastError { get; set; }

    public virtual DeliveryAppConnection Connection { get; set; } = null!;
    public virtual DeliveryAppStore Store { get; set; } = null!;
    public virtual DeliveryAppProductMapping ProductMapping { get; set; } = null!;
}

public record ExternalDeliveryOrderLine(
    string ExternalProductId,
    string Sku,
    string Name,
    string ItemType,
    int Quantity,
    int UnitPrice,
    string? Notes,
    IReadOnlyList<ExternalDeliveryOrderLine>? Subitems = null);

public record ExternalDeliveryDiscount(
    string? Title,
    string? Description,
    string? Type,
    string? Sku,
    int Value,
    int AmountByRappi,
    int AmountByPartner);
