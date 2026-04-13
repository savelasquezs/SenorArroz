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
        string sortOrder = "desc",
        CancellationToken cancellationToken = default);

    Task<IEnumerable<AppPayment>> GetByOrderIdAsync(int orderId, CancellationToken cancellationToken = default);
    Task<IEnumerable<AppPayment>> GetByAppIdAsync(int appId, CancellationToken cancellationToken = default);
    Task<IEnumerable<AppPayment>> GetUnsettledAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<AppPayment>> GetUnsettledByAppIdAsync(int appId, CancellationToken cancellationToken = default);
    Task<IEnumerable<AppPayment>> GetUnsettledByDateRangeAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);
    Task<AppPayment?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<AppPayment> CreateAsync(AppPayment appPayment, CancellationToken cancellationToken = default);
    Task<AppPayment> UpdateAsync(AppPayment appPayment, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default);

    // Settlement methods
    Task<bool> SettlePaymentsAsync(IEnumerable<int> paymentIds, CancellationToken cancellationToken = default);
    Task<bool> UnsettlePaymentsAsync(IEnumerable<int> paymentIds, CancellationToken cancellationToken = default);

    // Statistics
    Task<decimal> GetTotalAmountByAppAsync(int appId, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default);
    Task<decimal> GetTotalAmountByOrderAsync(int orderId, CancellationToken cancellationToken = default);
    Task<decimal> GetUnsettledAmountByAppAsync(int appId, CancellationToken cancellationToken = default);
    Task<int> GetTotalCountByAppAsync(int appId, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default);
    Task<int> GetUnsettledCountByAppAsync(int appId, CancellationToken cancellationToken = default);
}
