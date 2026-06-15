// SenorArroz.Domain/Interfaces/Repositories/IBankRepository.cs
using SenorArroz.Domain.Entities;
using SenorArroz.Shared.Models;

namespace SenorArroz.Domain.Interfaces.Repositories;

public interface IBankRepository
{
    Task<PagedResult<Bank>> GetPagedAsync(
        int? branchId = null,
        string? name = null,
        bool? active = null,
        bool excludeHiddenBanks = false,
        int page = 1,
        int pageSize = 10,
        string sortBy = "name",
        string sortOrder = "asc",
        CancellationToken cancellationToken = default);

    Task<IEnumerable<Bank>> GetByBranchIdAsync(int branchId, bool excludeHiddenBanks = false, CancellationToken cancellationToken = default);
    Task<Bank?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<Bank?> GetByIdWithAppsAsync(int id, CancellationToken cancellationToken = default);
    Task<Bank> CreateAsync(Bank bank, CancellationToken cancellationToken = default);
    Task<Bank> UpdateAsync(Bank bank, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> NameExistsInBranchAsync(string name, int branchId, int? excludeId = null, CancellationToken cancellationToken = default);

    // Statistics
    Task<int> GetTotalAppsAsync(int bankId, CancellationToken cancellationToken = default);
    Task<int> GetActiveAppsAsync(int bankId, CancellationToken cancellationToken = default);
    Task<decimal> GetTotalBankPaymentsAsync(int bankId, CancellationToken cancellationToken = default);
    Task<decimal> GetTotalReservationDepositsAsync(int bankId, DateTime? asOf = null, CancellationToken cancellationToken = default);
    Task<decimal> GetTotalExpenseBankPaymentsAsync(int bankId, CancellationToken cancellationToken = default);
    Task<decimal> GetTotalOutgoingTransfersAsync(int bankId, DateTime? asOf = null, CancellationToken cancellationToken = default);
    Task<decimal> GetTotalIncomingTransfersAsync(int bankId, DateTime? asOf = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ingresos al banco por abonos/liquidaciones de domiciliario vía transferencia (paridad con cuadre de caja).
    /// </summary>
    Task<decimal> GetTotalDeliverymanBankTransferInAsync(int bankId, DateTime? asOf = null, CancellationToken cancellationToken = default);

    Task<decimal> GetCurrentBalanceAsync(int bankId, CancellationToken cancellationToken = default);
    Task<decimal> GetBalanceAsOfAsync(int bankId, DateTime asOf, CancellationToken cancellationToken = default);

    /// <summary>Totales en rango UTC [fromUtc, toUtc] inclusive por CreatedAt.</summary>
    Task<decimal> GetTotalBankPaymentsInPeriodAsync(int bankId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);
    Task<decimal> GetTotalReservationDepositsInPeriodAsync(int bankId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);

    Task<decimal> GetTotalExpenseBankPaymentsInPeriodAsync(int bankId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);
    Task<decimal> GetTotalOutgoingTransfersInPeriodAsync(int bankId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);
    Task<decimal> GetTotalIncomingTransfersInPeriodAsync(int bankId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);
    Task<decimal> GetTotalDeliverymanBankTransferInPeriodAsync(int bankId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);
}
