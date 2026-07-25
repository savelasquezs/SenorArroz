using MediatR;
using SenorArroz.Application.Features.ReservationDeposits.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.ReservationDeposits.Queries;

public class GetDepositsByOrderHandler : IRequestHandler<GetDepositsByOrderQuery, List<ReservationDepositDto>>
{
    private readonly IReservationDepositRepository _depositRepository;
    private readonly SenorArroz.Application.Common.Interfaces.IBranchContext _branchContext;

    public GetDepositsByOrderHandler(
        IReservationDepositRepository depositRepository,
        SenorArroz.Application.Common.Interfaces.IBranchContext branchContext)
    {
        _depositRepository = depositRepository;
        _branchContext = branchContext;
    }

    public async Task<List<ReservationDepositDto>> Handle(GetDepositsByOrderQuery request, CancellationToken cancellationToken)
    {
        var deposits = await _depositRepository.GetByOrderIdAsync(request.OrderId, cancellationToken);
        foreach (var deposit in deposits)
            _branchContext.EnsureAccess(deposit.BranchId);

        return deposits.Select(d => new ReservationDepositDto
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
        }).ToList();
    }
}
