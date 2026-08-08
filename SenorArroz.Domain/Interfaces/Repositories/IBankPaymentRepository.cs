// SenorArroz.Domain/Interfaces/Repositories/IBankPaymentRepository.cs
using SenorArroz.Domain.Entities;
using SenorArroz.Shared.Models;

namespace SenorArroz.Domain.Interfaces.Repositories;

public interface IBankPaymentRepository
{
    Task<PagedResult<BankPayment>> GetPagedAsync(
        int? orderId = null,
        int? bankId = null,
        bool? verified = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        int page = 1,
        int pageSize = 10,
        string sortBy = "createdAt",
        string sortOrder = "desc",
        int? restrictToBankBranchId = null,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<BankPayment>> GetByOrderIdAsync(int orderId, CancellationToken cancellationToken = default);
    Task<IEnumerable<BankPayment>> GetByBankIdAsync(int bankId, CancellationToken cancellationToken = default);
    Task<IEnumerable<BankPayment>> GetUnverifiedAsync(CancellationToken cancellationToken = default);
    Task<BankPayment?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<BankPayment> CreateAsync(BankPayment bankPayment, CancellationToken cancellationToken = default);
    Task<BankPayment> UpdateAsync(BankPayment bankPayment, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default);

    // Verification methods
    Task<bool> VerifyPaymentAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> UnverifyPaymentAsync(int id, CancellationToken cancellationToken = default);
    Task<int> UnverifyPaymentsForBankInPeriodAsync(
        int bankId,
        DateTime fromUtc,
        DateTime toUtc,
        int? restrictToBankBranchId = null,
        CancellationToken cancellationToken = default);

    // Statistics
    Task<decimal> GetTotalAmountByBankAsync(int bankId, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default);
    Task<decimal> GetTotalAmountByOrderAsync(int orderId, CancellationToken cancellationToken = default);
    Task<int> GetTotalCountByBankAsync(int bankId, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default);
    Task<int> GetUnverifiedCountByBankAsync(int bankId, CancellationToken cancellationToken = default);
}
