using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public class Bank : BaseEntity
{
    public int BranchId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public bool Active { get; set; } = true;

    // Navigation Properties
    public virtual Branch Branch { get; set; } = null!;
    public virtual ICollection<App> Apps { get; set; } = new List<App>();
    public virtual ICollection<BankPayment> BankPayments { get; set; } = new List<BankPayment>();
    public virtual ICollection<ExpenseBankPayment> ExpenseBankPayments { get; set; } = new List<ExpenseBankPayment>();
}