using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.Customers.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Customers.Commands
{
    public class UpdateCustomerHandler : IRequestHandler<UpdateCustomerCommand, CustomerDto>
    {
        private readonly ICustomerRepository _customerRepository;
        private readonly IMapper _mapper;

        public UpdateCustomerHandler(ICustomerRepository customerRepository, IMapper mapper)
        {
            _customerRepository = customerRepository;
            _mapper = mapper;
        }

        public async Task<CustomerDto> Handle(UpdateCustomerCommand request, CancellationToken cancellationToken)
        {
            var customer = await _customerRepository.GetByIdAsync(request.Id);
            if (customer == null)
            {
                throw new NotFoundException($"Cliente con ID {request.Id} no encontrado");
            }

            // Validate phone doesn't exist for other customers
            if (await _customerRepository.PhoneExistsAsync(request.Phone1, customer.BranchId, request.Id))
            {
                throw new BusinessException($"Ya existe otro cliente con el teléfono {request.Phone1} en esta sucursal");
            }

            if (!string.IsNullOrEmpty(request.Phone2) &&
                await _customerRepository.PhoneExistsAsync(request.Phone2, customer.BranchId, request.Id))
            {
                throw new BusinessException($"Ya existe otro cliente con el teléfono {request.Phone2} en esta sucursal");
            }

            // Update customer
            customer.Name = request.Name.Trim();
            customer.Phone1 = request.Phone1;
            customer.Phone2 = request.Phone2;
            customer.Active = request.Active;

            customer = await _customerRepository.UpdateAsync(customer);

            // Return complete customer with addresses
            var updatedCustomer = await _customerRepository.GetByIdWithAddressesAsync(customer.Id);
            var customerDto = _mapper.Map<CustomerDto>(updatedCustomer);

            // Add additional data
            customerDto.TotalOrders = await _customerRepository.GetTotalOrdersAsync(customer.Id);
            customerDto.LastOrderDate = await _customerRepository.GetLastOrderDateAsync(customer.Id);

            return customerDto;
        }
    }
}