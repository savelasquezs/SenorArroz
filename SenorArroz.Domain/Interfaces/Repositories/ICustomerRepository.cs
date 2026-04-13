using SenorArroz.Domain.Entities;
using SenorArroz.Shared.Models;


namespace SenorArroz.Domain.Interfaces.Repositories
{
    public interface ICustomerRepository
    {
        Task<PagedResult<Customer>> GetPagedAsync(
            int? branchId,
            string? name = null,
            string? phone = null,
            bool? active = null,
            int page = 1,
            int pageSize = 10,
            string sortBy = "name",
            string sortOrder = "asc",
            CancellationToken cancellationToken = default);

        Task<Customer?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
        Task<Customer?> GetByIdWithAddressesAsync(int id, CancellationToken cancellationToken = default);
        Task<Customer?> GetByPhoneAsync(string phone, int branchId, CancellationToken cancellationToken = default);
        Task<IEnumerable<Customer>> GetByBranchIdAsync(int branchId, CancellationToken cancellationToken = default);
        Task<Customer> CreateAsync(Customer customer, CancellationToken cancellationToken = default);
        Task<Customer> UpdateAsync(Customer customer, CancellationToken cancellationToken = default);
        Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
        Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default);
        Task<bool> PhoneExistsAsync(string phone, int branchId, int? excludeId = null, CancellationToken cancellationToken = default);
        /// <summary>Pedidos no cancelados.</summary>
        Task<int> GetTotalOrdersAsync(int customerId, CancellationToken cancellationToken = default);
        /// <summary>
        /// Devuelve la fecha del primer y último pedido no cancelado en una sola query.
        /// Retorna (null, null) si el cliente no tiene pedidos.
        /// </summary>
        Task<(DateTime? First, DateTime? Last)> GetOrderDateRangeAsync(int customerId, CancellationToken cancellationToken = default);
        /// <summary>Suma de <c>Order.Total</c> en pedidos no cancelados.</summary>
        Task<int> GetTotalOrderRevenueAsync(int customerId, CancellationToken cancellationToken = default);
    }
}
