using System.Text.Json.Serialization;

namespace SenorArroz.Application.Features.ExpenseHeaders.DTOs;

public class CreateExpenseHeaderDto
{
    public int SupplierId { get; set; }

    /// <summary>Domiciliario al que se imputa el gasto (liquidación).</summary>
    public int? DeliverymanId { get; set; }

    public List<CreateExpenseDetailDto> ExpenseDetails { get; set; } = new();
    public List<CreateExpenseBankPaymentDto>? ExpenseBankPayments { get; set; }

    /// <summary>Incluir IVA 19 % sobre el subtotal de las líneas (total factura = subtotal + IVA).</summary>
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
}

public class CreateExpenseBankPaymentDto
{
    public int BankId { get; set; }
    public decimal Amount { get; set; }
}


