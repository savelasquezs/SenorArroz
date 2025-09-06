using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public class App : BaseEntity
{
    public int BankId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public bool Active { get; set; } = true;

    // Navigation Properties
    public virtual Bank Bank { get; set; } = null!;
    public virtual ICollection<AppPayment> AppPayments { get; set; } = new List<AppPayment>();
}
