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
        string sortOrder = "desc");

    Task<IEnumerable<BankPayment>> GetByOrderIdAsync(int orderId);
    Task<IEnumerable<BankPayment>> GetByBankIdAsync(int bankId);
    Task<IEnumerable<BankPayment>> GetUnverifiedAsync();
    Task<BankPayment?> GetByIdAsync(int id);
    Task<BankPayment> CreateAsync(BankPayment bankPayment);
    Task<BankPayment> UpdateAsync(BankPayment bankPayment);
    Task<bool> DeleteAsync(int id);
    Task<bool> ExistsAsync(int id);

    // Verification methods
    Task<bool> VerifyPaymentAsync(int id);
    Task<bool> UnverifyPaymentAsync(int id);

    // Statistics
    Task<decimal> GetTotalAmountByBankAsync(int bankId, DateTime? fromDate = null, DateTime? toDate = null);
    Task<decimal> GetTotalAmountByOrderAsync(int orderId);
    Task<int> GetTotalCountByBankAsync(int bankId, DateTime? fromDate = null, DateTime? toDate = null);
    Task<int> GetUnverifiedCountByBankAsync(int bankId);
}
