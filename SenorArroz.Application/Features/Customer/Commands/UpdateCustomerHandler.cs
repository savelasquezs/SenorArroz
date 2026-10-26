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

            // Current mobiles already belong to this customer; rechecking them on
            // every name/username edit triggers two unnecessary EXISTS queries.
            // Still check any genuinely new mobile across the current tenant.
            var ownedMobiles = new HashSet<string>(StringComparer.Ordinal);
            if (ColombianPhoneNormalizer.TryNormalize(customer.Phone1, out var existingPhone1))
                ownedMobiles.Add(existingPhone1);
            if (ColombianPhoneNormalizer.TryNormalize(customer.Phone2, out var existingPhone2))
                ownedMobiles.Add(existingPhone2);

            if (ColombianPhoneNormalizer.TryNormalize(request.Phone1, out var requestedPhone1)
                && !ownedMobiles.Contains(requestedPhone1)
                && await _customerRepository.PhoneExistsAsync(
                    request.Phone1!, _currentTenant.TenantId, request.Id, cancellationToken))
            {
                throw new BusinessException($"Ya existe otro cliente con el teléfono {request.Phone1} en este restaurante");
            }

            if (ColombianPhoneNormalizer.TryNormalize(request.Phone2, out var requestedPhone2)
                && !ownedMobiles.Contains(requestedPhone2)
                && await _customerRepository.PhoneExistsAsync(
                    request.Phone2!, _currentTenant.TenantId, request.Id, cancellationToken))
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

            // UpdateAsync already reloads the full customer with addresses after saving.
            // Reuse its returned graph instead of running the same split query again.
            var customerDto = _mapper.Map<CustomerDto>(customer);

            var orderStatistics = await _customerRepository.GetOrderStatisticsForCustomersAsync(
                new[] { customer.Id }, cancellationToken);
            orderStatistics.TryGetValue(customer.Id, out var stats);
            customerDto.ApplyOrderStatistics(stats);
            await _loyaltyCycle.ApplyLoyaltyPreviewsToCustomerDtosAsync(new[] { customerDto }, cancellationToken);

            return customerDto;
        }
    }
}
