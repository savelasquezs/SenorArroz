using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SenorArroz.Application.Features.Customers.DTOs
{
    public class NeighborhoodDto
    {
        public int Id { get; set; }
        public int BranchId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int DeliveryFee { get; set; }
        public bool Active { get; set; }
    }
}
