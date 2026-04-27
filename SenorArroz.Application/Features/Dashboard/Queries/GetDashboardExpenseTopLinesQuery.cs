using MediatR;
using SenorArroz.Application.Features.Dashboard.DTOs;

namespace SenorArroz.Application.Features.Dashboard.Queries;

public class GetDashboardExpenseTopLinesQuery : IRequest<DashboardExpenseTopLinesResponseDto>
{
    public DateTime FromUtc { get; set; }
    public DateTime ToUtc { get; set; }
    public int? BranchId { get; set; }
    /// <summary>ID de <see cref="SenorArroz.Domain.Entities.ExpenseCategory"/> (obligatorio y &gt; 0).</summary>
    public int CategoryId { get; set; }
    public int? ExpenseId { get; set; }
    /// <summary>1–500. Si no se envía o es inválido, se usa 15.</summary>
    public int? Limit { get; set; }
}
