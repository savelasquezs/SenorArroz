using SenorArroz.Domain.Entities;
using SenorArroz.Shared.Models;

namespace SenorArroz.Domain.Interfaces.Repositories;

public interface IReservationDepositRepository
{
    Task<ReservationDeposit> CreateAsync(ReservationDeposit deposit, CancellationToken cancellationToken = default);
    Task<ReservationDeposit?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<List<ReservationDeposit>> GetByOrderIdAsync(int orderId, CancellationToken cancellationToken = default);
    Task<decimal> GetTotalDepositedByOrderAsync(int orderId, CancellationToken cancellationToken = default);
    Task<int> DeleteByOrderIdAsync(int orderId, CancellationToken cancellationToken = default);
    Task<PagedResult<ReservationDeposit>> GetPagedAsync(
        int branchId,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        int? orderId = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);
}
