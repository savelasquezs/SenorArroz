using MediatR;
using SenorArroz.Application.Features.Banks.DTOs;

namespace SenorArroz.Application.Features.Banks.Queries;

public class GetBankLedgerPeriodQuery : IRequest<BankBalanceBreakdownDto?>
{
    public int BankId { get; set; }

    /// <summary>Fecha de calendario (solo parte fecha; se interpreta en hora Colombia).</summary>
    public DateTime FromDate { get; set; }

    public DateTime ToDate { get; set; }
}
