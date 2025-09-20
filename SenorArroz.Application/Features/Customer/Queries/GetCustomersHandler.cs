using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.Customers.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SenorArroz.Application.Features.Customers.Queries
{
    public class GetCustomersHandler : IRequestHandler<GetCustomersQuery, PagedResult<CustomerDto>>
    {
        private readonly ICustomerRepository _customerRepository;
        private readonly IMapper _mapper;

        public GetCustomersHandler(ICustomerRepository customerRepository, IMapper mapper)
        {
            _customerRepository = customerRepository;
            _mapper = mapper;
        }

        public async Task<PagedResult<CustomerDto>> Handle(GetCustomersQuery request, CancellationToken cancellationToken)
        {
            var pagedCustomers = await _customerRepository.GetPagedAsync(
                request.BranchId,
                request.Name,
                request.Phone,
                request.Active,
                request.Page,
                request.PageSize,
                request.SortBy,
                request.SortOrder);

            var customerDtos = new List<CustomerDto>();

            foreach (var customer in pagedCustomers.Items)
            {
                var customerDto = _mapper.Map<CustomerDto>(customer);

                // Add additional data
                customerDto.TotalOrders = await _customerRepository.GetTotalOrdersAsync(customer.Id);
                customerDto.LastOrderDate = await _customerRepository.GetLastOrderDateAsync(customer.Id);

                customerDtos.Add(customerDto);
            }

            return new PagedResult<CustomerDto>
            {
                Items = customerDtos,
                TotalCount = pagedCustomers.TotalCount,
                Page = pagedCustomers.Page,
                PageSize = pagedCustomers.PageSize,
                TotalPages = pagedCustomers.TotalPages
            };
        }
    }
}
