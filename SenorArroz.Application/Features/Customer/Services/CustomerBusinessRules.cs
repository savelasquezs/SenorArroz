using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Customers.Services;

public class CustomerBusinessRules
{
    private readonly ICustomerRepository _customerRepository;
    private readonly IUserRepository _userRepository;

    public CustomerBusinessRules(ICustomerRepository customerRepository, IUserRepository userRepository)
    {
        _customerRepository = customerRepository;
        _userRepository = userRepository;
    }

    public async Task ValidateBranchAccessAsync(int userId, int customerId, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null)
            throw new NotFoundException("Usuario no encontrado");

        if (user.Role == UserRole.Superadmin)
            return;

        var customer = await _customerRepository.GetByIdAsync(customerId, cancellationToken);
        if (customer == null)
            throw new NotFoundException("Cliente no encontrado");

        if (user.BranchId != customer.BranchId)
            throw new BusinessException("No tienes permisos para acceder a este cliente");
    }

    public async Task ValidateCustomerCanBeDeletedAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var totalOrders = await _customerRepository.GetTotalOrdersAsync(customerId, cancellationToken);
        if (totalOrders > 0)
        {
            throw new BusinessException("No se puede eliminar un cliente que tiene órdenes registradas. Use desactivar en su lugar.");
        }
    }

    public async Task ValidatePhoneUniquenessAsync(string phone, int branchId, int? excludeCustomerId = null)
    {
        if (await _customerRepository.PhoneExistsAsync(phone, branchId, excludeCustomerId))
        {
            throw new BusinessException($"Ya existe un cliente con el teléfono {phone} en esta sucursal");
        }
    }

    public void ValidateCustomerIsActive(Domain.Entities.Customer customer)
    {
        if (!customer.Active)
        {
            throw new BusinessException("El cliente está inactivo");
        }
    }

    public async Task<bool> CanUserManageCustomerAsync(int userId, int customerId, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null)
            return false;

        if (user.Role == UserRole.Superadmin)
            return true;

        if (user.Role is UserRole.Admin or UserRole.Cashier)
        {
            var customer = await _customerRepository.GetByIdAsync(customerId, cancellationToken);
            return customer?.BranchId == user.BranchId;
        }

        return false;
    }
}
