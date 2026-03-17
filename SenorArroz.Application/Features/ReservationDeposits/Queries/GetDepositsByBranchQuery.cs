using MediatR;
using SenorArroz.Application.Features.ReservationDeposits.DTOs;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.ReservationDeposits.Queries;

public class GetDepositsByBranchQuery : IRequest<PagedResult<ReservationDepositDto>>
{
    public int? BranchId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int? OrderId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
