using System.ComponentModel.DataAnnotations;

namespace SenorArroz.Application.Features.ReservationDeposits.DTOs;

public class CreateReservationDepositDto
{
    [Required]
    public int OrderId { get; set; }

    [Required, Range(1, double.MaxValue, ErrorMessage = "El monto debe ser mayor a 0")]
    public decimal Amount { get; set; }

    public bool IsEffective { get; set; }

    /// <summary>Requerido si IsEffective = false y no usa app</summary>
    public int? BankId { get; set; }

    /// <summary>Requerido si IsEffective = false y no usa banco</summary>
    public int? AppId { get; set; }

    public string? Notes { get; set; }
}
