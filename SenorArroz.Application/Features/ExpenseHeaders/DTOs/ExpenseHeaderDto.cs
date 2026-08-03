namespace SenorArroz.Application.Features.ExpenseHeaders.DTOs;

public class ExpenseHeaderDto
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public int SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string? SupplierPhone { get; set; }
    public decimal? Total { get; set; }

    /// <summary>IVA en pesos (19 % de las líneas gravadas).</summary>
    public decimal VatAmount { get; set; }

    /// <summary>Notas generales del comprobante.</summary>
    public string? Notes { get; set; }

    public int CreatedById { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public int? DeliverymanId { get; set; }
    public string? DeliverymanName { get; set; }

    /// <summary>Abono con <c>ExpenseHeaderId</c> apuntando a este gasto (descuento por gasto).</summary>
    public int? LinkedDeliverymanAdvanceId { get; set; }
    public decimal? LinkedDeliverymanAdvanceAmount { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<ExpenseDetailDto> ExpenseDetails { get; set; } = new();
    public List<ExpenseBankPaymentDto> ExpenseBankPayments { get; set; } = new();
    // Campos calculados para filtros locales
    public List<string> CategoryNames { get; set; } = new(); // Categorías únicas de los detalles
    public List<string> BankNames { get; set; } = new(); // Bancos de los pagos
    public List<string> ExpenseNames { get; set; } = new(); // Nombres de gastos en detalles
}

