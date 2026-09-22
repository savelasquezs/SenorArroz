using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public class Customer : TenantOwnedEntity
{
    /// <summary>Legacy physical column branch_id. It only records the branch where the customer was created.</summary>
    public int BranchId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Phone1 { get; set; }
    public string? Phone2 { get; set; }
    public string? WhatsAppUserId { get; set; }
    public string? WhatsAppUsername { get; set; }
    public bool Active { get; set; } = true;
    public bool WhatsAppTemplateOptIn { get; set; }
    public string? WhatsAppTemplateAuthorizationMessageId { get; set; }

    // Navigation Properties
    public virtual Branch Branch { get; set; } = null!;
    public virtual Tenant Tenant { get; set; } = null!;
    public virtual ICollection<CustomerPhone> Phones { get; set; } = new List<CustomerPhone>();
    public virtual ICollection<Address> Addresses { get; set; } = new List<Address>();
    public virtual ICollection<Order> Orders { get; set; } = new List<Order>();
}
