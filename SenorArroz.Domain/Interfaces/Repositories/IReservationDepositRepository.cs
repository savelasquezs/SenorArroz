using SenorArroz.Domain.Entities;
using SenorArroz.Shared.Models;

namespace SenorArroz.Domain.Interfaces.Repositories;

public interface IReservationDepositRepository
{
    Task<ReservationDeposit> CreateAsync(ReservationDeposit deposit);
    Task<ReservationDeposit?> GetByIdAsync(int id);
    Task<List<ReservationDeposit>> GetByOrderIdAsync(int orderId);
    Task<decimal> GetTotalDepositedByOrderAsync(int orderId);
    Task<PagedResult<ReservationDeposit>> GetPagedAsync(
        int branchId,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        int? orderId = null,
        int page = 1,
        int pageSize = 20);
}
