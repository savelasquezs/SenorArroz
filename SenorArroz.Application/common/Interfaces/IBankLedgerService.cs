using SenorArroz.Application.Features.Banks.DTOs;

namespace SenorArroz.Application.Common.Interfaces;

public interface IBankLedgerService
{
    /// <summary>
    /// Saldo acumulado histórico por banco con desglose (paridad con repositorio).
    /// </summary>
    Task<BankBalanceBreakdownDto> GetRunningBalanceBreakdownAsync(int bankId, CancellationToken cancellationToken = default);
}
