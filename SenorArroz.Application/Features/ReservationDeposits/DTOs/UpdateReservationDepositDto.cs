namespace SenorArroz.Application.Features.ReservationDeposits.DTOs;

/// <summary>Actualización de monto de un abono (mismo tope que al crear).</summary>
public class UpdateReservationDepositDto
{
    public decimal Amount { get; set; }
}
