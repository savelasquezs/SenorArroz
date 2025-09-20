using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SenorArroz.Application.Features.Customers.DTOs
{
    public class CustomerAddressDto
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }
        public int NeighborhoodId { get; set; }
        public string NeighborhoodName { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string? AdditionalInfo { get; set; }
        public int DeliveryFee { get; set; }
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public bool IsPrimary { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
