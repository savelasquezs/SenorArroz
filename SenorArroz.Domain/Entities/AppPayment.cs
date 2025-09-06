using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public class AppPayment : BaseEntity
{
    public int OrderId { get; set; }
    public int AppId { get; set; }
    public int Amount { get; set; }
    public bool IsSetted { get; set; } = false;

    // Navigation Properties
    public virtual Order Order { get; set; } = null!;
    public virtual App App { get; set; } = null!;
}