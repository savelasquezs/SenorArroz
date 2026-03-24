using MediatR;
using SenorArroz.Application.Features.Banks.DTOs;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.Banks.Queries;

public class GetBankDeliverymanTransferAdvancesPagedQuery : IRequest<PagedResult<DeliverymanBankAdvanceLineDto>?>
{
    public int BankId { get; set; }
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public int? BranchId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
