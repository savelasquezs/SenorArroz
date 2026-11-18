using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public class WhatsAppBranchSetting : TenantOwnedEntity
{
    public int BranchId { get; set; }
    public string PhoneNumberId { get; set; } = string.Empty;
    public string BusinessAccountId { get; set; } = string.Empty;
    public string DisplayPhoneNumber { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string WebhookVerifyToken { get; set; } = string.Empty;
    public string? AppSecret { get; set; }
    public bool IsActive { get; set; }
    public bool IsVerified { get; set; }
    public DateTime? LastVerifiedAt { get; set; }
    public bool AwayMessageEnabled { get; set; }
    public string? AwayMessageText { get; set; }
    public bool CoexistenceEnabled { get; set; }
    public string ConnectionState { get; set; } = "pending";
    public string? ConnectionError { get; set; }
    public DateTime? CoexistenceConnectedAt { get; set; }
    public string HistorySyncStatus { get; set; } = "not_started";
    public string ContactsSyncStatus { get; set; } = "not_started";
    public string? HistorySyncRequestId { get; set; }
    public string? ContactsSyncRequestId { get; set; }
    public DateTime? LastSyncAt { get; set; }

    public virtual Branch Branch { get; set; } = null!;
}
