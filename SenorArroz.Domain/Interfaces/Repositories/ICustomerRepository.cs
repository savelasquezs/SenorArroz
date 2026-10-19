using SenorArroz.Domain.Entities;
using SenorArroz.Shared.Models;


namespace SenorArroz.Domain.Interfaces.Repositories
{
    public interface ICustomerRepository
    {
        Task<PagedResult<Customer>> GetPagedAsync(
            int? branchId,
            string? search = null,
            string? name = null,
            string? phone = null,
            string? whatsAppUsername = null,
            bool? active = null,
            int page = 1,
            int pageSize = 10,
            string sortBy = "name",
            string sortOrder = "asc",
            CancellationToken cancellationToken = default);

        Task<Customer?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
        Task<Customer?> GetByIdWithAddressesAsync(int id, CancellationToken cancellationToken = default);
        Task<Customer?> GetByPhoneAsync(string phone, int tenantId, CancellationToken cancellationToken = default);
        Task<IEnumerable<Customer>> GetByBranchIdAsync(int branchId, CancellationToken cancellationToken = default);
        Task<Customer> CreateAsync(Customer customer, CancellationToken cancellationToken = default);
        Task<Customer> UpdateAsync(Customer customer, CancellationToken cancellationToken = default);
        Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
        Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default);
        Task<bool> PhoneExistsAsync(string phone, int tenantId, int? excludeId = null, CancellationToken cancellationToken = default);
        /// <summary>Pedidos no cancelados.</summary>
        Task<int> GetTotalOrdersAsync(int customerId, CancellationToken cancellationToken = default);
        Task<IReadOnlyDictionary<int, CustomerOrderStatistics>> GetOrderStatisticsForCustomersAsync(IReadOnlyCollection<int> customerIds, CancellationToken cancellationToken = default);
    }
    public sealed record CustomerOrderStatistics(int TotalOrders, DateTime? FirstOrderDate, DateTime? LastOrderDate, int TotalAccumulated, int DeliveredOrders);
}