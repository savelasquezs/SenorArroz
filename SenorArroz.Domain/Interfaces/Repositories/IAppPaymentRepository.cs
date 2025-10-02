// SenorArroz.Domain/Interfaces/Repositories/IAppPaymentRepository.cs
using SenorArroz.Domain.Entities;
using SenorArroz.Shared.Models;

namespace SenorArroz.Domain.Interfaces.Repositories;

public interface IAppPaymentRepository
{
    Task<PagedResult<AppPayment>> GetPagedAsync(
        int? orderId = null,
        int? appId = null,
        bool? settled = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        int page = 1,
        int pageSize = 10,
        string sortBy = "createdAt",
        string sortOrder = "desc");

    Task<IEnumerable<AppPayment>> GetByOrderIdAsync(int orderId);
    Task<IEnumerable<AppPayment>> GetByAppIdAsync(int appId);
    Task<IEnumerable<AppPayment>> GetUnsettledAsync();
    Task<IEnumerable<AppPayment>> GetUnsettledByAppIdAsync(int appId);
    Task<IEnumerable<AppPayment>> GetUnsettledByDateRangeAsync(DateTime fromDate, DateTime toDate);
    Task<AppPayment?> GetByIdAsync(int id);
    Task<AppPayment> CreateAsync(AppPayment appPayment);
    Task<AppPayment> UpdateAsync(AppPayment appPayment);
    Task<bool> DeleteAsync(int id);
    Task<bool> ExistsAsync(int id);

    // Settlement methods
    Task<bool> SettlePaymentsAsync(IEnumerable<int> paymentIds);
    Task<bool> UnsettlePaymentsAsync(IEnumerable<int> paymentIds);

    // Statistics
    Task<decimal> GetTotalAmountByAppAsync(int appId, DateTime? fromDate = null, DateTime? toDate = null);
    Task<decimal> GetTotalAmountByOrderAsync(int orderId);
    Task<decimal> GetUnsettledAmountByAppAsync(int appId);
    Task<int> GetTotalCountByAppAsync(int appId, DateTime? fromDate = null, DateTime? toDate = null);
    Task<int> GetUnsettledCountByAppAsync(int appId);
}
