namespace SenorArroz.Application.Features.BankTransfers.DTOs;

public class BankTransferDto
{
    public int Id { get; set; }
    public int? FromBankId { get; set; }
    public string FromBankName { get; set; } = string.Empty;
    public int? ToBankId { get; set; }
    public string ToBankName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Note { get; set; }
    public int CreatedById { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
