using System.ComponentModel.DataAnnotations;

namespace SenorArroz.Application.Features.BankTransfers.DTOs;

public class CreateBankTransferDto
{
    [Required(ErrorMessage = "El banco de origen es requerido")]
    public int FromBankId { get; set; }

    [Required(ErrorMessage = "El banco de destino es requerido")]
    public int ToBankId { get; set; }

    [Required(ErrorMessage = "El monto es requerido")]
    [Range(0.01, double.MaxValue, ErrorMessage = "El monto debe ser mayor a 0")]
    public decimal Amount { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }
}
