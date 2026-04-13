using SenorArroz.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SenorArroz.Domain.Interfaces.Repositories
{
    public interface IAddressRepository
    {
        Task<IEnumerable<Address>> GetByCustomerIdAsync(int customerId, CancellationToken cancellationToken = default);
        Task<Address?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
        Task<Address?> GetPrimaryByCustomerIdAsync(int customerId, CancellationToken cancellationToken = default);
        Task<Address> CreateAsync(Address address, CancellationToken cancellationToken = default);
        Task<Address> UpdateAsync(Address address, CancellationToken cancellationToken = default);
        Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
        Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default);
        Task<bool> SetPrimaryAddressAsync(int customerId, int addressId, CancellationToken cancellationToken = default);
        Task<bool> UnsetPrimaryAddressesAsync(int customerId, CancellationToken cancellationToken = default);
    }
}
