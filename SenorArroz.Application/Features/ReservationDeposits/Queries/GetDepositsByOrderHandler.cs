using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Exceptions;
using MediatR;
using SenorArroz.Application.Features.ReservationDeposits.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.ReservationDeposits.Queries;

public class GetDepositsByOrderHandler : IRequestHandler<GetDepositsByOrderQuery, List<ReservationDepositDto>>
{
    private readonly IReservationDepositRepository _depositRepository;
    private readonly IOrderRepository? _orders;
    private readonly ICurrentUser? _currentUser;
    private readonly SenorArroz.Application.Common.Interfaces.IBranchContext _branchContext;

    public GetDepositsByOrderHandler(
        IReservationDepositRepository depositRepository,
        SenorArroz.Application.Common.Interfaces.IBranchContext branchContext, IOrderRepository? orders = null, ICurrentUser? currentUser = null)
    {
        _depositRepository = depositRepository;
        _branchContext = branchContext;
        _orders = orders;
        _currentUser = currentUser;
    }

    public async Task<List<ReservationDepositDto>> Handle(GetDepositsByOrderQuery request, CancellationToken cancellationToken)
    {
        var parent = _orders is null ? null : await _orders.GetByIdAsync(request.OrderId, cancellationToken);
        if (_orders is not null && parent is null) throw new NotFoundException("Pedido no encontrado");
        if (parent is not null && _currentUser is not null)
            OrderAccessRules.EnsureAccess(parent, _currentUser, _branchContext);
        var deposits = await _depositRepository.GetByOrderIdAsync(request.OrderId, cancellationToken);
        if (parent is null || _currentUser is null)
            foreach (var deposit in deposits) _branchContext.EnsureAccess(deposit.BranchId);

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
