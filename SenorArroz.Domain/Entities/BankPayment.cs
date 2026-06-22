using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public class BankPayment : BaseEntity
{
    public int OrderId { get; set; }
    public int BankId { get; set; }
    public decimal Amount { get; set; }
    /// <summary>Si fue creado al promover un abono de reserva, referencia el abono original.</summary>
    public int? SourceReservationDepositId { get; set; }
    /// <summary>Marca pagos bancarios creados al liquidar dinero retenido por apps.</summary>
    public bool IsAppSettlement { get; set; } = false;
    /// <summary>JSON con IDs de AppPayment incluidos en la liquidación que creó este ingreso bancario.</summary>
    public string? AppSettlementSourcePaymentIds { get; set; }
    public bool IsVerified { get; set; } = false;
    public DateTime? VerifiedAt { get; set; }

    // Navigation Properties
    public virtual Order Order { get; set; } = null!;
    public virtual Bank Bank { get; set; } = null!;
}
