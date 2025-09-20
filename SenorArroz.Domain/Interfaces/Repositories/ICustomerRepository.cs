using SenorArroz.Domain.Entities;
using SenorArroz.Shared.Models;


namespace SenorArroz.Domain.Interfaces.Repositories
{
    public interface ICustomerRepository
    {
        Task<PagedResult<Customer>> GetPagedAsync(
            int branchId,
            string? name = null,
            string? phone = null,
            bool? active = null,
            int page = 1,
            int pageSize = 10,
            string sortBy = "name",
            string sortOrder = "asc");

        Task<Customer?> GetByIdAsync(int id);
        Task<Customer?> GetByIdWithAddressesAsync(int id);
        Task<Customer?> GetByPhoneAsync(string phone, int branchId);
        Task<IEnumerable<Customer>> GetByBranchIdAsync(int branchId);
        Task<Customer> CreateAsync(Customer customer);
        Task<Customer> UpdateAsync(Customer customer);
        Task<bool> DeleteAsync(int id);
        Task<bool> ExistsAsync(int id);
        Task<bool> PhoneExistsAsync(string phone, int branchId, int? excludeId = null);
        Task<int> GetTotalOrdersAsync(int customerId);
        Task<DateTime?> GetLastOrderDateAsync(int customerId);
    }
}
