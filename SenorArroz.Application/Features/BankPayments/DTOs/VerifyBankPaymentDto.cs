// SenorArroz.Application/Features/BankPayments/DTOs/VerifyBankPaymentDto.cs
using System.ComponentModel.DataAnnotations;

namespace SenorArroz.Application.Features.BankPayments.DTOs;

public class VerifyBankPaymentDto
{
    [Required(ErrorMessage = "La fecha de verificación es requerida")]
    public DateTime VerifiedAt { get; set; } = DateTime.UtcNow;
}
