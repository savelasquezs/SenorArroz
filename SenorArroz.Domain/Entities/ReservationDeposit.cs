using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

/// <summary>
/// Registra un abono/anticipo recibido para una reserva futura.
/// El dinero se contabiliza en el cuadre del día en que se recibe (ReceivedAt),
/// no en el día en que se entrega el pedido.
/// </summary>
public class ReservationDeposit : BaseEntity
{
    public int OrderId { get; set; }
    public int BranchId { get; set; }
    public decimal Amount { get; set; }

    /// <summary>true = efectivo, false = banco o app</summary>
    public bool IsEffective { get; set; }

    /// <summary>Banco que recibió el pago (si IsEffective = false y no es app)</summary>
    public int? BankId { get; set; }

    /// <summary>App que recibió el pago (si IsEffective = false y es app)</summary>
    public int? AppId { get; set; }

    /// <summary>Momento exacto en que el dinero entró a caja (se usa para el cuadre)</summary>
    public DateTime ReceivedAt { get; set; }

    /// <summary>Usuario que recibió el abono</summary>
    public int ReceivedById { get; set; }

    public string? Notes { get; set; }

    // Navigation Properties
    public virtual Order Order { get; set; } = null!;
    public virtual Branch Branch { get; set; } = null!;
    public virtual Bank? Bank { get; set; }
    public virtual App? App { get; set; }
    public virtual User ReceivedBy { get; set; } = null!;
}
