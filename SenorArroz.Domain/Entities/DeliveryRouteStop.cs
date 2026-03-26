using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

/// <summary>
/// Parada de ruta vinculada a un pedido; conserva snapshot de dirección y buffer justificado.
/// </summary>
public class DeliveryRouteStop : BaseEntity
{
    public int DeliveryRouteId { get; set; }
    public int OrderId { get; set; }
    public int StopSequence { get; set; }

    /// <summary>Texto usado para detectar torre/bloque/unidad y auditoría.</summary>
    public string? AddressSnapshotText { get; set; }

    public bool RequiresComplexAccessBuffer { get; set; }
    /// <summary>Ej.: torre, bloque, unidad (primera coincidencia).</summary>
    public string? ComplexAccessMatchTerm { get; set; }
    public int ComplexAccessBonusSeconds { get; set; }

    public virtual DeliveryRoute DeliveryRoute { get; set; } = null!;
    public virtual Order Order { get; set; } = null!;
}
