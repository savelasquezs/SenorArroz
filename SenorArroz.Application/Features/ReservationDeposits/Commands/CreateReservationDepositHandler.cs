using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.ReservationDeposits.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.ReservationDeposits.Commands;

public class CreateReservationDepositHandler : IRequestHandler<CreateReservationDepositCommand, ReservationDepositDto>
{
    private readonly IReservationDepositRepository _depositRepository;
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;

    public CreateReservationDepositHandler(
        IReservationDepositRepository depositRepository,
        IApplicationDbContext context,
        ICurrentUser currentUser)
    {
        _depositRepository = depositRepository;
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<ReservationDepositDto> Handle(CreateReservationDepositCommand request, CancellationToken cancellationToken)
    {
        if (request.Amount <= 0)
            throw new BusinessException("El monto debe ser mayor a 0");

        if (!request.IsEffective && request.BankId == null && request.AppId == null)
            throw new BusinessException("Debe especificar banco o app para un abono no efectivo");

        var order = await _context.Orders
            .FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken);

        if (order == null)
            throw new BusinessException("El pedido no existe");

        if (order.Type != OrderType.Reservation)
            throw new BusinessException("Solo se pueden registrar abonos para pedidos de tipo reserva");

        if (order.Status == OrderStatus.Delivered || order.Status == OrderStatus.Cancelled)
            throw new BusinessException("No se puede abonar a un pedido ya finalizado");

        var totalDeposited = await _depositRepository.GetTotalDepositedByOrderAsync(request.OrderId);
        if (totalDeposited + request.Amount > order.Total)
            throw new BusinessException($"El total abonado ({totalDeposited + request.Amount:C}) supera el valor del pedido ({order.Total:C})");

        var deposit = new ReservationDeposit
        {
            OrderId = request.OrderId,
            BranchId = order.BranchId,
            Amount = request.Amount,
            IsEffective = request.IsEffective,
            BankId = request.BankId,
            AppId = request.AppId,
            ReceivedAt = DateTime.UtcNow,
            ReceivedById = _currentUser.Id,
            Notes = request.Notes
        };

        var created = await _depositRepository.CreateAsync(deposit);

        return new ReservationDepositDto
        {
            Id = created.Id,
            OrderId = created.OrderId,
            BranchId = created.BranchId,
            Amount = created.Amount,
            IsEffective = created.IsEffective,
            BankId = created.BankId,
            BankName = created.Bank?.Name,
            AppId = created.AppId,
            AppName = created.App?.Name,
            ReceivedAt = created.ReceivedAt,
            ReceivedById = created.ReceivedById,
            ReceivedByName = created.ReceivedBy?.Name ?? string.Empty,
            Notes = created.Notes,
            CreatedAt = created.CreatedAt
        };
    }
}
