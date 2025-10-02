// SenorArroz.Application/Features/AppPayments/DTOs/CreateAppPaymentDto.cs
using System.ComponentModel.DataAnnotations;

namespace SenorArroz.Application.Features.AppPayments.DTOs;

public class CreateAppPaymentDto
{
    [Required(ErrorMessage = "El pedido es requerido")]
    public int OrderId { get; set; }

    [Required(ErrorMessage = "La app es requerida")]
    public int AppId { get; set; }

    [Required(ErrorMessage = "El monto es requerido")]
    [Range(0.01, double.MaxValue, ErrorMessage = "El monto debe ser mayor a 0")]
    public decimal Amount { get; set; }
}
