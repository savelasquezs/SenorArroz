using SenorArroz.Domain.Entities.Common;

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
}

