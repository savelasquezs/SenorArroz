using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public class ExpenseHeader : BaseEntity
{
    public int BranchId { get; set; }
    public int SupplierId { get; set; }
    public int CreatedById { get; set; }

    /// <summary>Gasto imputado a un domiciliario (liquidación / abonos).</summary>
    public int? DeliverymanId { get; set; }

    public decimal? Total { get; set; } // Suma líneas + VatAmount (recalculado en BD)

    /// <summary>IVA en pesos cuando aplica (p. ej. 19 % sobre subtotal de líneas).</summary>
    public decimal VatAmount { get; set; }

    // Navigation Properties
    public virtual Branch Branch { get; set; } = null!;
    public virtual Supplier Supplier { get; set; } = null!;
    public virtual User CreatedBy { get; set; } = null!;
    public virtual User? Deliveryman { get; set; }
    public virtual ICollection<ExpenseDetail> ExpenseDetails { get; set; } = new List<ExpenseDetail>();
    public virtual ICollection<ExpenseBankPayment> ExpenseBankPayments { get; set; } = new List<ExpenseBankPayment>();
}