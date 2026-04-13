using SenorArroz.Domain.Entities;


namespace SenorArroz.Domain.Interfaces.Repositories
{
    public interface INeighborhoodRepository
    {
        Task<IEnumerable<Neighborhood>> GetByBranchIdAsync(int branchId, CancellationToken cancellationToken = default);
        Task<Neighborhood?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
        Task<Neighborhood> CreateAsync(Neighborhood neighborhood, CancellationToken cancellationToken = default);
        Task<Neighborhood> UpdateAsync(Neighborhood neighborhood, CancellationToken cancellationToken = default);
        Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
        Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default);
        Task<bool> NameExistsAsync(string name, int branchId, int? excludeId = null, CancellationToken cancellationToken = default);
        Task<int> GetTotalCustomersAsync(int id, CancellationToken cancellationToken = default);
        Task<int> GetTotalAddressesAsync(int id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Totales por barrio en 2 consultas (evita N+1 en detalle de sucursal).
        /// </summary>
        Task<IReadOnlyDictionary<int, (int TotalCustomers, int TotalAddresses)>> GetNeighborhoodStatsBulkAsync(
            IReadOnlyCollection<int> neighborhoodIds,
            CancellationToken cancellationToken = default);
    }
}
