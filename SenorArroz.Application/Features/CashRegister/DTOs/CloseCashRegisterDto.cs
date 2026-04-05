using System.ComponentModel.DataAnnotations;

namespace SenorArroz.Application.Features.CashRegister.DTOs;

public class CloseCashRegisterDto
{
    [Required]
    public DateTime ClosedAt { get; set; }

    [Required]
    public string DenominationCounts { get; set; } = "{}";

    [Required]
    [Range(0, double.MaxValue)]
    public decimal ClosingCash { get; set; }

    [Required]
    public List<CloseBankReconciliationDto> BankReconciliations { get; set; } = new();
}

public class CloseBankReconciliationDto
{
    [Required]
    public int BankId { get; set; }
    public decimal ExpectedBalance { get; set; }
    [Required]
    public decimal ActualBalance { get; set; }
    public string Adjustments { get; set; } = "[]";
}
