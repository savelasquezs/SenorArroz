using System.ComponentModel.DataAnnotations;

namespace SenorArroz.Application.Features.BankTransfers.DTOs;

public class CreateBankTransferDto
{
    /// <summary>Null o ausente = origen es efectivo de caja.</summary>
    public int? FromBankId { get; set; }

    /// <summary>Null o ausente = destino es efectivo de caja.</summary>
    public int? ToBankId { get; set; }

    [Required(ErrorMessage = "El monto es requerido")]
    [Range(0.01, double.MaxValue, ErrorMessage = "El monto debe ser mayor a 0")]
    public decimal Amount { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }
}
