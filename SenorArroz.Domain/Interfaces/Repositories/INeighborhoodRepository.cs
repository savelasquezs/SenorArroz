using SenorArroz.Domain.Entities;


namespace SenorArroz.Domain.Interfaces.Repositories
{
    public interface INeighborhoodRepository
    {
        Task<IEnumerable<Neighborhood>> GetByBranchIdAsync(int branchId);
        Task<Neighborhood?> GetByIdAsync(int id);
        Task<Neighborhood> CreateAsync(Neighborhood neighborhood);
        Task<Neighborhood> UpdateAsync(Neighborhood neighborhood);
        Task<bool> DeleteAsync(int id);
        Task<bool> ExistsAsync(int id);
        Task<bool> NameExistsAsync(string name, int branchId, int? excludeId = null);
        Task<int> GetTotalCustomersAsync(int id);
        Task<int> GetTotalAddressesAsync(int id);
    }
}
