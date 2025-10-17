using System.ComponentModel.DataAnnotations;

namespace SenorArroz.Application.Features.BankPayments.DTOs;

public class UpdateBankPaymentDto
{
    [Required(ErrorMessage = "El monto es requerido")]
    [Range(0.01, double.MaxValue, ErrorMessage = "El monto debe ser mayor a 0")]
    public decimal Amount { get; set; }
}

