using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public class BankPayment : BaseEntity
{
    public int OrderId { get; set; }
    public int BankId { get; set; }
    public decimal Amount { get; set; }
    public bool IsVerified { get; set; } = false;
    public DateTime? VerifiedAt { get; set; }

    // Navigation Properties
    public virtual Order Order { get; set; } = null!;
    public virtual Bank Bank { get; set; } = null!;
}