using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Common;

/// <summary>
/// Prepara DeliverymanAdvance antes de DbSet.Update cuando se cargo con Include.
/// Las navegaciones son referencias existentes; solo se persisten sus FK escalares.
/// </summary>
public static class DeliverymanAdvanceUpdateGraphForPersistence
{
    public static void DetachReadOnlyNavigations(DeliverymanAdvance advance)
    {
        advance.Deliveryman = null!;
        advance.Creator = null!;
        advance.Branch = null!;
        advance.Bank = null;
        advance.ExpenseHeader = null;
    }
}
