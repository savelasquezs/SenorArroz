using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Features.Customers.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Customers.Commands
{
    public class UpdateCustomerHandler : IRequestHandler<UpdateCustomerCommand, CustomerDto>
    {
        private readonly ICustomerRepository _customerRepository;
        private readonly IMapper _mapper;
        private readonly ILoyaltyCycleService _loyaltyCycle;
        private readonly ICurrentTenant _currentTenant;

        public UpdateCustomerHandler(
            ICustomerRepository customerRepository,
            IMapper mapper,
            ILoyaltyCycleService loyaltyCycle,
            ICurrentTenant currentTenant)
        {
            _customerRepository = customerRepository;
            _mapper = mapper;
            _loyaltyCycle = loyaltyCycle;
            _currentTenant = currentTenant;
        }

        public async Task<CustomerDto> Handle(UpdateCustomerCommand request, CancellationToken cancellationToken)
        {
            var customer = await _customerRepository.GetByIdAsync(request.Id, cancellationToken);
            if (customer == null)
            {
                throw new NotFoundException($"Cliente con ID {request.Id} no encontrado");
            }

            // Validate phone doesn't exist for other customers
            if (!string.IsNullOrWhiteSpace(request.Phone1)
                && ColombianPhoneNormalizer.TryNormalize(request.Phone1, out _)
                && await _customerRepository.PhoneExistsAsync(request.Phone1, _currentTenant.TenantId, request.Id))
            {
                throw new BusinessException($"Ya existe otro cliente con el teléfono {request.Phone1} en este restaurante");
            }

            if (!string.IsNullOrEmpty(request.Phone2)
                && ColombianPhoneNormalizer.TryNormalize(request.Phone2, out _)
                &&
                await _customerRepository.PhoneExistsAsync(request.Phone2, _currentTenant.TenantId, request.Id))
            {
                throw new BusinessException($"Ya existe otro cliente con el teléfono {request.Phone2} en este restaurante");
            }

            // Update customer
            var normalizedPhone1 = string.IsNullOrWhiteSpace(request.Phone1) ? null : ColombianPhoneNormalizer.NormalizeCustomerContact(request.Phone1);
            var normalizedPhone2 = string.IsNullOrWhiteSpace(request.Phone2) ? null : ColombianPhoneNormalizer.NormalizeCustomerContact(request.Phone2);
            if (normalizedPhone2 == normalizedPhone1)
                normalizedPhone2 = null;
            customer.Name = request.Name.Trim();
            customer.Phone1 = normalizedPhone1;
            customer.Phone2 = normalizedPhone2;
            customer.WhatsAppUsername = WhatsAppIdentityNormalizer.NormalizeUsername(request.WhatsAppUsername);
            customer.Active = request.Active;

            customer = await _customerRepository.UpdateAsync(customer, cancellationToken);

            // Return complete customer with addresses
            var updatedCustomer = await _customerRepository.GetByIdWithAddressesAsync(customer.Id, cancellationToken);
            var customerDto = _mapper.Map<CustomerDto>(updatedCustomer);

            // Add additional data
            customerDto.TotalOrders = await _customerRepository.GetTotalOrdersAsync(customer.Id, cancellationToken);
            var (first, last) = await _customerRepository.GetOrderDateRangeAsync(customer.Id, cancellationToken);
            customerDto.FirstOrderDate = first;
            customerDto.LastOrderDate = last;
            customerDto.TotalAccumulated = await _customerRepository.GetTotalOrderRevenueAsync(customer.Id, cancellationToken);
            await _loyaltyCycle.ApplyLoyaltyPreviewToCustomerDtoAsync(customerDto, cancellationToken);

            return customerDto;
        }
    }
}
