using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.CashRegister.DTOs;

public class CashRegisterExpectedDto
{
    public decimal OpeningCash { get; set; }
    public decimal ExpectedCash { get; set; }
    public decimal CashFromOrders { get; set; }

    /// <summary>Suma de totales de pedidos entregados en el período (venta cobrada según el pedido).</summary>
    public decimal DeliveredOrdersSalesTotal { get; set; }

    /// <summary>Pagos registrados por banco en esos pedidos (dinero que compone el cuadre bancario).</summary>
    public decimal BankPaymentsFromOrdersTotal { get; set; }

    /// <summary>Pagos por apps en esos pedidos (no en caja ni en banco del negocio de inmediato).</summary>
    public decimal AppPaymentsFromOrdersTotal { get; set; }

    public decimal CashDeposits { get; set; }
    public decimal CashExpenses { get; set; }

    /// <summary>
    /// Abonos a domiciliarios por transferencia en el período: se restan del efectivo esperado
    /// porque ese monto ya computa en el cuadre bancario.
    /// </summary>
    public decimal AdvancesBankTransfer { get; set; }

    /// <summary>
    /// Suma de montos de préstamos informales activos en la sucursal. Se resta del efectivo esperado
    /// (ExpectedCash ya incluye esta resta).
    /// </summary>
    public decimal InformalLoansActiveTotal { get; set; }

    /// <summary>Abonos a Caja Mayor Efectivo en el período (efectivo que sale del cajón).</summary>
    public decimal CashVaultAbonosTotal { get; set; }

    /// <summary>Descargas desde Caja Mayor Efectivo en el período (efectivo que vuelve al cajón).</summary>
    public decimal CashVaultDescargasTotal { get; set; }

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
