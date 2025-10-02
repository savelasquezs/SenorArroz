// SenorArroz.Application/Features/AppPayments/DTOs/SettleAppPaymentsDto.cs
using System.ComponentModel.DataAnnotations;

namespace SenorArroz.Application.Features.AppPayments.DTOs;

public class SettleAppPaymentsDto
{
    [Required(ErrorMessage = "Se requiere al menos un ID de pago")]
    [MinLength(1, ErrorMessage = "Se requiere al menos un ID de pago")]
    public List<int> PaymentIds { get; set; } = new();
}
