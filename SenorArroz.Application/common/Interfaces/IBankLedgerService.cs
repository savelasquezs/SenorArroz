using SenorArroz.Application.Features.Banks.DTOs;

namespace SenorArroz.Application.Common.Interfaces;

public interface IBankLedgerService
{
    /// <summary>
    /// Saldo acumulado histórico por banco con desglose (paridad con repositorio).
    /// </summary>
    Task<BankBalanceBreakdownDto> GetRunningBalanceBreakdownAsync(int bankId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Movimiento neto en el rango UTC [fromUtc, toUtc] (misma fórmula por componente que el histórico).
    /// </summary>
    Task<BankBalanceBreakdownDto> GetPeriodBalanceBreakdownAsync(int bankId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);
}
