namespace SenorArroz.Application.Features.ReservationDeposits.DTOs;

public class ReservationDepositDto
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int BranchId { get; set; }
    public decimal Amount { get; set; }
    public bool IsEffective { get; set; }
    public int? BankId { get; set; }
    public string? BankName { get; set; }
    public int? AppId { get; set; }
    public string? AppName { get; set; }
    public DateTime ReceivedAt { get; set; }
    public int ReceivedById { get; set; }
    public string ReceivedByName { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
}
