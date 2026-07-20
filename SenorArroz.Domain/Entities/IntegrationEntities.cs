using SenorArroz.Domain.Entities.Common;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Domain.Entities;

public class DeliveryAppConnection : BaseEntity
{
    public int BranchId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Environment { get; set; } = "sandbox";
    public string DisplayName { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string EncryptedClientSecret { get; set; } = string.Empty;
    public string ExternalStoreId { get; set; } = string.Empty;
    public int FinancialAppId { get; set; }
    public int DefaultCookingTimeMinutes { get; set; } = 30;
    public string EncryptedWebhookSecret { get; set; } = string.Empty;
    public bool WebhookConfigured { get; set; }
    public bool IsActive { get; set; }
    public bool IsVerified { get; set; }
    public DateTime? LastVerifiedAt { get; set; }
    public DateTime? LastCatalogSyncAt { get; set; }
    public string? LastError { get; set; }

    public virtual Branch Branch { get; set; } = null!;
    public virtual App FinancialApp { get; set; } = null!;
    public virtual ICollection<DeliveryAppProductMapping> ProductMappings { get; set; } = [];
    public virtual ICollection<ExternalDeliveryOrder> ExternalOrders { get; set; } = [];
}

public class DeliveryAppProductMapping : BaseEntity
{
    public int ConnectionId { get; set; }
    public string ExternalProductId { get; set; } = string.Empty;
    public string ExternalSku { get; set; } = string.Empty;
    public string ExternalName { get; set; } = string.Empty;
    public string ItemType { get; set; } = "product";
    public bool IsActive { get; set; } = true;
    public int? ProductId { get; set; }

    public virtual DeliveryAppConnection Connection { get; set; } = null!;
    public virtual Product? Product { get; set; }
}

public class ExternalDeliveryOrder : BaseEntity
{
    public int ConnectionId { get; set; }
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
    public int CookingTimeMinutes { get; set; }
    public string RawPayloadJson { get; set; } = "{}";
    public string LinesJson { get; set; } = "[]";
    public int? InternalOrderId { get; set; }
    public int? AcceptedByUserId { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public string? LastError { get; set; }

    public virtual DeliveryAppConnection Connection { get; set; } = null!;
    public virtual Order? InternalOrder { get; set; }
}

public class IntegrationWebhookEvent : BaseEntity
{
    public int ConnectionId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string EventKey { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public string Status { get; set; } = "received";
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
    public DateTime? ProcessedAt { get; set; }
}

public record ExternalDeliveryOrderLine(
    string ExternalProductId,
    string Sku,
    string Name,
    string ItemType,
    int Quantity,
    int UnitPrice,
    string? Notes);
