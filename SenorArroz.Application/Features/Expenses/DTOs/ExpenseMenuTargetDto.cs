using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.Expenses.DTOs;

public class ExpenseMenuTargetInputDto
{
    public ExpenseMenuTargetType TargetType { get; set; }
    public int TargetId { get; set; }
}

public class ExpenseMenuTargetDto
{
    public ExpenseMenuTargetType TargetType { get; set; }
    public int TargetId { get; set; }
    public string TargetName { get; set; } = string.Empty;
    /// <summary>True si el destino es un producto sin <c>weight_grams</c> en catálogo (no aporta al reporte por gramos).</summary>
    public bool ProductMissingWeight { get; set; }
}
