using MediatR;
using SenorArroz.Application.Features.Customers.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SenorArroz.Application.Features.Customers.Queries
{
    public class GetCustomerByPhoneQuery : IRequest<CustomerDto?>
    {
        public string Phone { get; set; } = string.Empty;
        public int BranchId { get; set; }
    }
}
