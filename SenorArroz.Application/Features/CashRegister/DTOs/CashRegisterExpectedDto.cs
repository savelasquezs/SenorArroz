using SenorArroz.Domain.Enums;
using System.Text.Json.Serialization;

namespace SenorArroz.Application.Features.CashRegister.DTOs;

public class CashRegisterExpectedDto
{
    /// <summary>Efectivo físico al cierre del último cuadre (solo referencia de apertura de cajón).</summary>
    public decimal OpeningCash { get; set; }

    /// <summary>
    /// Total global al inicio del período: último cierre (efectivo + saldos reales por banco) + snapshot de préstamos informales
    /// + suma del snapshot de apps pendientes por liquidar guardado en ese cierre.
    /// </summary>
    public decimal OpeningGlobalTotal { get; set; }

    /// <summary>Suma del snapshot de apps pendientes incluida en <see cref="OpeningGlobalTotal"/> (0 si el cierre anterior no tenía snapshot).</summary>
    public decimal OpeningUnsettledAppsTotal { get; set; }

    /// <summary>
    /// Suma neta de pedidos entregados cuyo instante contable (PrepareAt o CreatedAt) cae en el período, descontando abonos de reserva ya registrados por ReceivedAt.
    /// </summary>
    public decimal SalesInPeriodTotal { get; set; }

    /// <summary>Suma de totales de gastos (ExpenseHeader.Total) en el período.</summary>
    public decimal ExpensesInPeriodTotal { get; set; }

    /// <summary>
    /// <see cref="OpeningGlobalTotal"/> + ventas del período − gastos del período
    /// + <see cref="ReservationDepositsAddedToGlobalTotal"/> + <see cref="BankPaymentsAddedToGlobalTotal"/>.
    /// </summary>
    public decimal ExpectedGlobalTotal { get; set; }

    /// <summary>
    /// Abonos de reserva recibidos en el período por <c>ReceivedAt</c>; se suman al total esperado global como movimiento real de dinero.
    /// </summary>
    public decimal ReservationDepositsAddedToGlobalTotal { get; set; }

    /// <summary>
    /// Transferencias recibidas después del último cuadre por <c>BankPayment.CreatedAt</c>, excluyendo abonos de reserva.
    /// </summary>
    public decimal BankPaymentsAddedToGlobalTotal { get; set; }

    /// <summary>Suma de préstamos informales activos (referencia para el conteo al cerrar; no se suma de nuevo al esperado).</summary>
    public decimal InformalLoansActiveTotal { get; set; }

    /// <summary>
    /// Pedidos de la sucursal que aún no están entregados ni cancelados (no se puede cerrar caja si hay alguno).
    /// </summary>
    public int UndeliveredOrdersCount { get; set; }

    public DateTime AsOf { get; set; }
    public DateTime? LastClosureAt { get; set; }
    public List<BankExpectedBalanceDto> Banks { get; set; } = new();

    [JsonIgnore]
    public List<BankExpectedBalanceDto> HiddenBanksForClosureCarry { get; set; } = new();

    /// <summary>Pagos vía app no liquidados ahora (pedidos entregados), desglosado por app.</summary>
    public List<UnsettledAppLineDto> UnsettledAppLines { get; set; } = new();

    /// <summary>Suma de <see cref="UnsettledAppLines"/>; entra en el total global contado al cerrar.</summary>
    public decimal UnsettledAppsTotal { get; set; }
}

public class UnsettledAppLineDto
{
    public int AppId { get; set; }
    public string AppName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class BankExpectedBalanceDto
{
    public int BankId { get; set; }
    public string BankName { get; set; } = string.Empty;
    public BankType BankType { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal ExpectedBalance { get; set; }
}
