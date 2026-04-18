using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.CashRegister.DTOs;

public class CashRegisterExpectedDto
{
    /// <summary>Efectivo físico al cierre del último cuadre (solo referencia de apertura de cajón).</summary>
    public decimal OpeningCash { get; set; }

    /// <summary>
    /// Total global al inicio del período: último cierre (efectivo + saldos reales por banco) + snapshot de préstamos informales en ese cierre.
    /// Si el cierre anterior no tenía snapshot de préstamos, solo caja + bancos.
    /// </summary>
    public decimal OpeningGlobalTotal { get; set; }

    /// <summary>
    /// Suma de totales de pedidos entregados cuyo instante contable (PrepareAt o CreatedAt) cae en el período.
    /// </summary>
    public decimal SalesInPeriodTotal { get; set; }

    /// <summary>Suma de totales de gastos (ExpenseHeader.Total) en el período.</summary>
    public decimal ExpensesInPeriodTotal { get; set; }

    /// <summary>
    /// <see cref="OpeningGlobalTotal"/> + ventas del período − gastos del período (= C0+B0+L0 + ventas − gastos).
    /// </summary>
    public decimal ExpectedGlobalTotal { get; set; }

    /// <summary>Suma de préstamos informales activos (referencia para el conteo al cerrar; no se suma de nuevo al esperado).</summary>
    public decimal InformalLoansActiveTotal { get; set; }

    /// <summary>
    /// Pedidos de la sucursal que aún no están entregados ni cancelados (no se puede cerrar caja si hay alguno).
    /// </summary>
    public int UndeliveredOrdersCount { get; set; }

    public DateTime AsOf { get; set; }
    public DateTime? LastClosureAt { get; set; }
    public List<BankExpectedBalanceDto> Banks { get; set; } = new();
}

public class BankExpectedBalanceDto
{
    public int BankId { get; set; }
    public string BankName { get; set; } = string.Empty;
    public BankType BankType { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal ExpectedBalance { get; set; }
}
