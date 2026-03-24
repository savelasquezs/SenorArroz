namespace SenorArroz.Application.Features.Banks.DTOs;

public class DeliverymanBankAdvanceLineDto
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; }
    public int DeliverymanId { get; set; }
    public string DeliverymanName { get; set; } = string.Empty;
    public int BranchId { get; set; }
    public string? Notes { get; set; }
}
