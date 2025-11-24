using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Customers.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Customers.Queries;

public class GetCustomerAddressByIdHandler : IRequestHandler<GetCustomerAddressByIdQuery, CustomerAddressDto?>
{
    private readonly IAddressRepository _addressRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public GetCustomerAddressByIdHandler(
        IAddressRepository addressRepository,
        ICustomerRepository customerRepository,
        IMapper mapper,
        ICurrentUser currentUser)
    {
        _addressRepository = addressRepository;
        _customerRepository = customerRepository;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<CustomerAddressDto?> Handle(GetCustomerAddressByIdQuery request, CancellationToken cancellationToken)
    {
        // Get the address
        var address = await _addressRepository.GetByIdAsync(request.AddressId);
        if (address == null)
        {
            return null;
        }

        // Verify customer exists and get branch info for security check
        var customer = await _customerRepository.GetByIdAsync(address.CustomerId);
        if (customer == null)
        {
            throw new BusinessException("El cliente asociado a esta dirección no existe");
        }

        // Check if user has access to this customer's branch
        if (_currentUser.Role != "superadmin" && customer.BranchId != _currentUser.BranchId)
        {
            throw new BusinessException("No tienes permisos para acceder a esta dirección");
        }

        return _mapper.Map<CustomerAddressDto>(address);
    }
}

