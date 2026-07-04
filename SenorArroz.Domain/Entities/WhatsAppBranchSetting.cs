using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public class WhatsAppBranchSetting : BaseEntity
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

    public virtual Branch Branch { get; set; } = null!;
}
