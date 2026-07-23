using SenorArroz.Domain.Entities.Common;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Domain.Entities;

public class User : BaseEntity
{
    public int BranchId { get; set; }
    public UserRole? Role { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool Active { get; set; } = true;
    public string? ProfileImageUrl { get; set; }

    /// <summary>
    /// Sesión exclusiva vigente para la app de domiciliarios. Los tokens sin
    /// este identificador solo se aceptan mientras el usuario no haya iniciado
    /// una sesión exclusiva después del despliegue de esta funcionalidad.
    /// </summary>
    public Guid? ActiveSessionId { get; set; }

    /// <summary>Ítem de catálogo <c>expense</c> usado solo para préstamos/gastos de quincena de esta persona.</summary>
    public int? PayrollExpenseId { get; set; }

    // Navigation Properties
    public virtual Branch Branch { get; set; } = null!;
    public virtual Expense? PayrollExpense { get; set; }
    public virtual ICollection<Order> TakenOrders { get; set; } = new List<Order>();
    public virtual ICollection<Order> DeliveryOrders { get; set; } = new List<Order>();
    public virtual ICollection<ExpenseHeader> CreatedExpenseHeaders { get; set; } = new List<ExpenseHeader>();
}
