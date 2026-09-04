using SenorArroz.Domain.Entities.Common;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Domain.Entities;

public sealed class WompiPaymentIntegration : BaseEntity
{
    public int TenantId { get; set; }
    public int BranchId { get; set; }
    public int FinancialAppId { get; set; }
    public string ActiveEnvironment { get; set; } = "sandbox";
    public bool IsEnabled { get; set; }
    public decimal EstimatedCommissionRate { get; set; }
    public string? SandboxPublicKey { get; set; }
    public string? SandboxEncryptedIntegritySecret { get; set; }
    public string? SandboxEncryptedEventsSecret { get; set; }
    public string? ProductionPublicKey { get; set; }
    public string? ProductionEncryptedIntegritySecret { get; set; }
    public string? ProductionEncryptedEventsSecret { get; set; }
    public DateTime? LastSandboxWebhookAt { get; set; }
    public DateTime? LastProductionWebhookAt { get; set; }
    public DateTime? LastTestedAt { get; set; }
    public string? LastError { get; set; }

    public Branch Branch { get; set; } = null!;
    public App FinancialApp { get; set; } = null!;
    public ICollection<WompiPaymentAttempt> PaymentAttempts { get; set; } = [];
}

public sealed class WompiPaymentAttempt : BaseEntity
{
    public int TenantId { get; set; }
    public int? OrderId { get; set; }
    public int? StorefrontCheckoutId { get; set; }
    public int IntegrationId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string Environment { get; set; } = "sandbox";
    public string PublicKeySnapshot { get; set; } = string.Empty;
    public string IntegritySignature { get; set; } = string.Empty;
    public string EncryptedEventsSecretSnapshot { get; set; } = string.Empty;
    public long ExpectedAmountInCents { get; set; }
    public string Currency { get; set; } = "COP";
    public PaymentAttemptStatus Status { get; set; } = PaymentAttemptStatus.Pending;
    public DateTime ExpiresAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public bool RequiresManualReview { get; set; }
    public string? ManualReviewReason { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public int? ReviewedByUserId { get; set; }
    public int? AppPaymentId { get; set; }

    public Order? Order { get; set; }
    public StorefrontCheckout? StorefrontCheckout { get; set; }
    public WompiPaymentIntegration Integration { get; set; } = null!;
    public AppPayment? AppPayment { get; set; }
    public User? ReviewedByUser { get; set; }
    public ICollection<WompiProviderTransaction> ProviderTransactions { get; set; } = [];
}

public sealed class StorefrontCheckout : BaseEntity
{
    public int TenantId { get; set; }
    public string PublicId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public int BranchId { get; set; }
    public int? CustomerId { get; set; }
    public int? SavedAddressId { get; set; }
    public int? OrderId { get; set; }
    public int? WhatsAppConversationId { get; set; }
    public string OrderSource { get; set; } = "web";
    public string CustomerPhone { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string FulfillmentType { get; set; } = "delivery";
    public string? AddressLabel { get; set; }
    public string? OriginalAddress { get; set; }
    public string? FormattedAddress { get; set; }
    public string? AddressAdditionalInfo { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public int DeliveryFee { get; set; }
    public int Subtotal { get; set; }
    public int DiscountTotal { get; set; }
    public int Total { get; set; }
    public string ItemsJson { get; set; } = "[]";
    public string? OrderNotes { get; set; }
    public OrderBenefitType AppliedBenefitType { get; set; }
    public int? AppliedBenefitSourceId { get; set; }
    public string? AppliedBenefitLabel { get; set; }
    public LoyaltyRewardType? AppliedBenefitRewardType { get; set; }
    public decimal? AppliedBenefitAmount { get; set; }
    public string? AppliedBenefitSnapshot { get; set; }
    public string Status { get; set; } = "pending";
    public DateTime ExpiresAt { get; set; }

    public Branch Branch { get; set; } = null!;
    public Customer? Customer { get; set; }
    public Address? SavedAddress { get; set; }
    public Order? Order { get; set; }
    public WhatsAppConversation? WhatsAppConversation { get; set; }
    public ICollection<WompiPaymentAttempt> PaymentAttempts { get; set; } = [];
}

public sealed record StorefrontCheckoutLine(
    int ProductId,
    int Quantity,
    int UnitPrice,
    int Discount,
    int Subtotal,
    string? Notes);

public sealed class WompiProviderTransaction : BaseEntity
{
    public int PaymentAttemptId { get; set; }
    public string ProviderTransactionId { get; set; } = string.Empty;
    public string Status { get; set; } = "PENDING";
    public string? PaymentMethod { get; set; }
    public long AmountInCents { get; set; }
    public string Currency { get; set; } = "COP";
    public string PayloadHash { get; set; } = string.Empty;
    public DateTime ObservedAt { get; set; }

    public WompiPaymentAttempt PaymentAttempt { get; set; } = null!;
}

public sealed class WompiWebhookEvent : BaseEntity
{
    public int TenantId { get; set; }
    public int IntegrationId { get; set; }
    public string Environment { get; set; } = string.Empty;
    public string EventFingerprint { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public string? ProviderTransactionId { get; set; }
    public string Status { get; set; } = "processed";
    public string? LastError { get; set; }
    public DateTime? ProcessedAt { get; set; }

    public WompiPaymentIntegration Integration { get; set; } = null!;
}

public sealed class PaymentNotificationOutboxMessage : BaseEntity
{
    public int TenantId { get; set; }
    public int BranchId { get; set; }
    public int OrderId { get; set; }
    public string EventType { get; set; } = "order_payment_approved";
    public string Status { get; set; } = "pending";
    public int AttemptCount { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public string? LastError { get; set; }

    public Order Order { get; set; } = null!;
}
