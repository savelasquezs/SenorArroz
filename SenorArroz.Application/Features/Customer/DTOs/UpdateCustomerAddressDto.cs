using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SenorArroz.Application.Features.Customers.DTOs
{
    public class UpdateCustomerAddressDto
    {
        [Required(ErrorMessage = "El barrio es requerido")]
        public int NeighborhoodId { get; set; }

        [Required(ErrorMessage = "La dirección es requerida")]
        [StringLength(200, ErrorMessage = "La dirección no puede exceder 200 caracteres")]
        public string Address { get; set; } = string.Empty;

        [StringLength(150, ErrorMessage = "La información adicional no puede exceder 150 caracteres")]
        public string? AdditionalInfo { get; set; }

        [Range(-90, 90, ErrorMessage = "La latitud debe estar entre -90 y 90")]
        public decimal? Latitude { get; set; }

        [Range(-180, 180, ErrorMessage = "La longitud debe estar entre -180 y 180")]
        public decimal? Longitude { get; set; }

        public bool IsPrimary { get; set; } = false;
    }

}
