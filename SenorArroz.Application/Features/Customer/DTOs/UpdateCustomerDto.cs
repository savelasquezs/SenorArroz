using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SenorArroz.Application.Features.Customers.DTOs
{
    public class UpdateCustomerDto
    {
        [Required(ErrorMessage = "El nombre es requerido")]
        [StringLength(150, ErrorMessage = "El nombre no puede exceder 150 caracteres")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "El teléfono principal es requerido")]
        [StringLength(10, MinimumLength = 10, ErrorMessage = "El teléfono debe tener exactamente 10 dígitos")]
        [RegularExpression(@"^\d{10}$", ErrorMessage = "El teléfono debe contener solo números")]
        public string Phone1 { get; set; } = string.Empty;

        [StringLength(10, MinimumLength = 10, ErrorMessage = "El teléfono secundario debe tener exactamente 10 dígitos")]
        [RegularExpression(@"^\d{10}$", ErrorMessage = "El teléfono secundario debe contener solo números")]
        public string? Phone2 { get; set; }

        public bool Active { get; set; } = true;
    }

}
