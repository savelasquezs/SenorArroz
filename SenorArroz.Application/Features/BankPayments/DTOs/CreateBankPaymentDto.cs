// SenorArroz.Application/Features/BankPayments/DTOs/CreateBankPaymentDto.cs
using System.ComponentModel.DataAnnotations;

namespace SenorArroz.Application.Features.BankPayments.DTOs;

public class CreateBankPaymentDto
{
    [Required(ErrorMessage = "El pedido es requerido")]
    public int OrderId { get; set; }

    [Required(ErrorMessage = "El banco es requerido")]
    public int BankId { get; set; }

    [Required(ErrorMessage = "El monto es requerido")]
    [Range(0.01, double.MaxValue, ErrorMessage = "El monto debe ser mayor a 0")]
    public decimal Amount { get; set; }
}
