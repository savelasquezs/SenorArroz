using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.ReservationDeposits.DTOs;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.ReservationDeposits.Commands;

public class UpdateReservationDepositHandler : IRequestHandler<UpdateReservationDepositCommand, ReservationDepositDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IReservationDepositRepository _depositRepository;

    public UpdateReservationDepositHandler(
        IApplicationDbContext context,
        IReservationDepositRepository depositRepository)
    {
        _context = context;
        _depositRepository = depositRepository;
    }

    public async Task<ReservationDepositDto> Handle(UpdateReservationDepositCommand request, CancellationToken cancellationToken)
    {
        if (request.Amount <= 0)
            throw new BusinessException("El monto debe ser mayor a 0");

        await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var deposit = await _context.ReservationDeposits
                .Include(d => d.Order)
                .FirstOrDefaultAsync(d => d.Id == request.Id, cancellationToken);

            if (deposit == null)
                throw new BusinessException("El abono no existe");

            var order = deposit.Order;
            if (order == null)
                throw new BusinessException("El pedido asociado no existe");

            if (order.Type != OrderType.Reservation)
                throw new BusinessException("Solo aplican abonos de reserva");

            if (order.Status == OrderStatus.Delivered || order.Status == OrderStatus.Cancelled)
                throw new BusinessException("No se puede modificar abonos de un pedido ya finalizado");

            var totalDeposited = await _depositRepository.GetTotalDepositedByOrderAsync(order.Id, cancellationToken);
            var totalWithoutThis = totalDeposited - deposit.Amount;
            if (totalWithoutThis + request.Amount > order.Total)
                throw new BusinessException(
                    $"El total abonado ({totalWithoutThis + request.Amount:C}) supera el valor del pedido ({order.Total:C})");

            deposit.Amount = request.Amount;

            if (deposit.BankId.HasValue && !deposit.IsEffective && !deposit.AppId.HasValue)
            {
                var linkedBankPayment = await _context.BankPayments
                    .FirstOrDefaultAsync(bp => bp.SourceReservationDepositId == deposit.Id, cancellationToken);

                if (linkedBankPayment != null)
                    linkedBankPayment.Amount = request.Amount;
            }

            await _context.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            var refreshed = await _depositRepository.GetByIdAsync(deposit.Id, cancellationToken);
            if (refreshed == null)
                throw new BusinessException("No se pudo leer el abono actualizado");

            return new ReservationDepositDto
            {
                Id = refreshed.Id,
                OrderId = refreshed.OrderId,
                BranchId = refreshed.BranchId,
                Amount = refreshed.Amount,
                IsEffective = refreshed.IsEffective,
                BankId = refreshed.BankId,
                BankName = refreshed.Bank?.Name,
                AppId = refreshed.AppId,
                AppName = refreshed.App?.Name,
                ReceivedAt = refreshed.ReceivedAt,
                ReceivedById = refreshed.ReceivedById,
                ReceivedByName = refreshed.ReceivedBy?.Name ?? string.Empty,
                Notes = refreshed.Notes,
                CreatedAt = refreshed.CreatedAt
            };
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
