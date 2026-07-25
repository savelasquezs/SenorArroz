using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.BankPayments.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.BankPayments.Commands;

public class UpdateBankPaymentHandler : IRequestHandler<UpdateBankPaymentCommand, BankPaymentDto>
{
    private readonly IBankPaymentRepository _bankPaymentRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IOrderBusinessRulesService _businessRules;
    private readonly ICurrentUser _currentUser;
    private readonly IBranchContext _branchContext;
    private readonly IMapper _mapper;
    private readonly IApplicationDbContext _context;

    public UpdateBankPaymentHandler(
        IBankPaymentRepository bankPaymentRepository,
        IOrderRepository orderRepository,
        IOrderBusinessRulesService businessRules,
        ICurrentUser currentUser,
        IBranchContext branchContext,
        IMapper mapper,
        IApplicationDbContext context)
    {
        _bankPaymentRepository = bankPaymentRepository;
        _orderRepository = orderRepository;
        _businessRules = businessRules;
        _currentUser = currentUser;
        _branchContext = branchContext;
        _mapper = mapper;
        _context = context;
    }

    public async Task<BankPaymentDto> Handle(UpdateBankPaymentCommand request, CancellationToken cancellationToken)
    {
        // Validar que el pago existe
        var bankPayment = await _bankPaymentRepository.GetByIdAsync(request.Id, cancellationToken);
        if (bankPayment == null)
            throw new BusinessException("Pago bancario no encontrado");

        // Obtener el pedido asociado
        var order = await _orderRepository.GetByIdAsync(bankPayment.OrderId, cancellationToken);
        if (order == null)
            throw new BusinessException("Pedido asociado no encontrado");
        _branchContext.EnsureAccess(order.BranchId);
        _branchContext.EnsureAccess(bankPayment.Bank.BranchId);

        // Validar permisos para modificar pagos
        if (!_businessRules.CanModifyPayments(order, _currentUser.Role))
            throw new BusinessException("No tienes permisos para modificar pagos de este pedido");

        // Validar acceso a sucursal
        if (!Roles.IsSuperadmin(_currentUser.Role) && bankPayment.Bank.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para modificar pagos de esta sucursal");

        await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            bankPayment.Amount = request.Amount;

            var updatedPayment = await _bankPaymentRepository.UpdateAsync(bankPayment, cancellationToken);

            if (updatedPayment.SourceReservationDepositId.HasValue)
            {
                var sourceDeposit = await _context.ReservationDeposits
                    .FirstOrDefaultAsync(d => d.Id == updatedPayment.SourceReservationDepositId.Value, cancellationToken);

                if (sourceDeposit == null)
                    throw new BusinessException("No se pudo encontrar el abono de reserva original");

                sourceDeposit.Amount = request.Amount;
                await _context.SaveChangesAsync(cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
            return _mapper.Map<BankPaymentDto>(updatedPayment);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }
}

