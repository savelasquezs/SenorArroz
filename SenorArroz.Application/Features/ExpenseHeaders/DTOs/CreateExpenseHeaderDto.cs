using System.Text.Json.Serialization;

namespace SenorArroz.Application.Features.ExpenseHeaders.DTOs;

public class CreateExpenseHeaderDto
{
    public int SupplierId { get; set; }

    /// <summary>Domiciliario al que se imputa el gasto (liquidación).</summary>
    public int? DeliverymanId { get; set; }

    public List<CreateExpenseDetailDto> ExpenseDetails { get; set; } = new();
    public List<CreateExpenseBankPaymentDto>? ExpenseBankPayments { get; set; }

    /// <summary>Notas generales del comprobante (opcional).</summary>
    public string? Notes { get; set; }

    /// <summary>Atajo para incluir IVA 19 % en todas las líneas.</summary>
    public bool IncludeVat { get; set; }
}

public class CreateExpenseDetailDto
{
    public int ExpenseId { get; set; }
    public decimal Quantity { get; set; }
    public int Amount { get; set; }
    /// <summary>Total de línea según factura (prioridad sobre cantidad × unitario).</summary>
    [JsonPropertyName("total")]
    public decimal? Total { get; set; }

    /// <summary>Aplicar IVA 19 % a esta línea.</summary>
    public bool IncludeVat { get; set; }

    public string? Notes { get; set; }
}

public class CreateExpenseBankPaymentDto
{
    public int BankId { get; set; }
    public decimal Amount { get; set; }
}
