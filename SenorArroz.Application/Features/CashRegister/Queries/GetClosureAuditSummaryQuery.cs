using MediatR;
using SenorArroz.Application.Features.CashRegister.DTOs;

namespace SenorArroz.Application.Features.CashRegister.Queries;

public class GetClosureAuditSummaryQuery : IRequest<CashClosureAuditSummaryDto?>
{
    public int Id { get; set; }
}
