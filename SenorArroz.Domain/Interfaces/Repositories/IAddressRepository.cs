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
        Task<IEnumerable<Address>> GetByCustomerIdAsync(int customerId);
        Task<Address?> GetByIdAsync(int id);
        Task<Address?> GetPrimaryByCustomerIdAsync(int customerId);
        Task<Address> CreateAsync(Address address);
        Task<Address> UpdateAsync(Address address);
        Task<bool> DeleteAsync(int id);
        Task<bool> ExistsAsync(int id);
        Task<bool> SetPrimaryAddressAsync(int customerId, int addressId);
        Task<bool> UnsetPrimaryAddressesAsync(int customerId);
    }
}
