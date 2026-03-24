using SenorArroz.Domain.Entities.Common;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Domain.Entities;

/// <summary>
/// Representa un abono/adelanto realizado a un domiciliario
/// </summary>
public class DeliverymanAdvance : BaseEntity
{
    /// <summary>
    /// ID del domiciliario que recibe el abono
    /// </summary>
    public int DeliverymanId { get; set; }

    /// <summary>
    /// Monto del abono en pesos colombianos
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// Cómo se registra el abono para el cuadre de caja.
    /// </summary>
    public DeliverymanAdvancePaymentMethod PaymentMethod { get; set; } = DeliverymanAdvancePaymentMethod.Cash;

    /// <summary>
    /// Banco destino si <see cref="PaymentMethod"/> es transferencia.
    /// </summary>
    public int? BankId { get; set; }

    /// <summary>
    /// Gasto vinculado si el abono es por descuento por gasto del domiciliario.
    /// </summary>
    public int? ExpenseHeaderId { get; set; }

    /// <summary>
    /// Notas o comentarios adicionales sobre el abono
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// ID del usuario (admin/cajero) que creó el abono
    /// </summary>
    public int CreatedBy { get; set; }

    /// <summary>
    /// ID de la sucursal donde se realizó el abono
    /// </summary>
    public int BranchId { get; set; }

    // Navigation Properties
    /// <summary>
    /// Domiciliario que recibe el abono
    /// </summary>
    public virtual User Deliveryman { get; set; } = null!;

    /// <summary>
    /// Usuario que creó el abono
    /// </summary>
    public virtual User Creator { get; set; } = null!;

    /// <summary>
    /// Sucursal donde se realizó el abono
    /// </summary>
    public virtual Branch Branch { get; set; } = null!;

    public virtual Bank? Bank { get; set; }

    public virtual ExpenseHeader? ExpenseHeader { get; set; }
}

