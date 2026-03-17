using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.ReservationDeposits.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.ReservationDeposits.Queries;

public class GetDepositsByBranchHandler : IRequestHandler<GetDepositsByBranchQuery, PagedResult<ReservationDepositDto>>
{
    private readonly IReservationDepositRepository _depositRepository;
    private readonly ICurrentUser _currentUser;

    public GetDepositsByBranchHandler(IReservationDepositRepository depositRepository, ICurrentUser currentUser)
    {
        _depositRepository = depositRepository;
        _currentUser = currentUser;
    }

    public async Task<PagedResult<ReservationDepositDto>> Handle(GetDepositsByBranchQuery request, CancellationToken cancellationToken)
    {
        int branchId = request.BranchId ?? _currentUser.BranchId;

        var result = await _depositRepository.GetPagedAsync(
            branchId,
            fromDate: request.FromDate,
            toDate: request.ToDate,
            orderId: request.OrderId,
            page: request.Page,
            pageSize: request.PageSize);

        return new PagedResult<ReservationDepositDto>
        {
            Items = result.Items.Select(d => new ReservationDepositDto
            {
                Id = d.Id,
                OrderId = d.OrderId,
                BranchId = d.BranchId,
                Amount = d.Amount,
                IsEffective = d.IsEffective,
                BankId = d.BankId,
                BankName = d.Bank?.Name,
                AppId = d.AppId,
                AppName = d.App?.Name,
                ReceivedAt = d.ReceivedAt,
                ReceivedById = d.ReceivedById,
                ReceivedByName = d.ReceivedBy?.Name ?? string.Empty,
                Notes = d.Notes,
                CreatedAt = d.CreatedAt
            }).ToList(),
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize,
            TotalPages = result.TotalPages
        };
    }
}
