namespace SenorArroz.Application.Features.Banks.DTOs;

/// <summary>
/// Desglose del saldo acumulado del banco (misma fórmula que el saldo actual en repositorio).
/// </summary>
public class BankBalanceBreakdownDto
{
    /// <summary>Ingresos por pagos bancarios de pedidos.</summary>
    public decimal BankPaymentsIn { get; set; }

    /// <summary>Salidas por pagos de gastos cargados a este banco.</summary>
    public decimal ExpenseBankPaymentsOut { get; set; }

    /// <summary>Transferencias enviadas desde este banco.</summary>
    public decimal OutgoingTransfers { get; set; }

    /// <summary>Transferencias recibidas en este banco.</summary>
    public decimal IncomingTransfers { get; set; }

    /// <summary>Abonos/liquidaciones de domiciliarios por transferencia a este banco.</summary>
    public decimal DeliverymanBankTransferIn { get; set; }

    /// <summary>Saldo neto: ingresos - gastos - salidas + entradas + domiciliarios (transferencia).</summary>
    public decimal NetBalance { get; set; }
}
