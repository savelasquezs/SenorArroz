using System.Text.Json.Serialization;

namespace SenorArroz.Application.Features.ExpenseHeaders.DTOs;

public class UpdateExpenseHeaderDto
{
    public int? SupplierId { get; set; }

    /// <summary>Domiciliario al que se imputa el gasto.</summary>
    public int? DeliverymanId { get; set; }

    public string? Notes { get; set; }

    public List<UpdateExpenseDetailDto>? ExpenseDetails { get; set; }
    public List<CreateExpenseBankPaymentDto>? ExpenseBankPayments { get; set; }

    /// <summary>Atajo para incluir IVA 19 % en todas las líneas.</summary>
    public bool IncludeVat { get; set; }
}

public class UpdateExpenseDetailDto
{
    public int? Id { get; set; } // null si es nuevo
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


