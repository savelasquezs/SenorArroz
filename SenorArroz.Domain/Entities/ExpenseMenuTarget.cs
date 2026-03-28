using SenorArroz.Domain.Entities.Common;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Domain.Entities;

/// <summary>Enlaza un gasto de catálogo a categorías o productos de menú para reparto proporcional por gramos vendidos.</summary>
public class ExpenseMenuTarget : BaseEntity
{
    public int ExpenseId { get; set; }
    public ExpenseMenuTargetType TargetType { get; set; }
    /// <summary>Id de <see cref="ProductCategory"/> o <see cref="Product"/> según <see cref="TargetType"/>.</summary>
    public int TargetId { get; set; }

    public virtual Expense Expense { get; set; } = null!;
}
