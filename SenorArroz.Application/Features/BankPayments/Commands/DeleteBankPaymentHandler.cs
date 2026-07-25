// SenorArroz.Application/Features/BankPayments/Commands/DeleteBankPaymentHandler.cs
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.BankPayments.Commands;

public class DeleteBankPaymentHandler : IRequestHandler<DeleteBankPaymentCommand, bool>
{
    private readonly IBankPaymentRepository _bankPaymentRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IOrderBusinessRulesService _businessRules;
    private readonly ICurrentUser _currentUser;
    private readonly IBranchContext _branchContext;

    public DeleteBankPaymentHandler(
        IBankPaymentRepository bankPaymentRepository,
        IOrderRepository orderRepository,
        IOrderBusinessRulesService businessRules,
        ICurrentUser currentUser,
        IBranchContext branchContext)
    {
        _bankPaymentRepository = bankPaymentRepository;
        _orderRepository = orderRepository;
        _businessRules = businessRules;
        _currentUser = currentUser;
        _branchContext = branchContext;
    }

    public async Task<bool> Handle(DeleteBankPaymentCommand request, CancellationToken cancellationToken)
    {
        var bankPayment = await _bankPaymentRepository.GetByIdAsync(request.Id, cancellationToken);
        if (bankPayment == null)
            return false;

        var order = await _orderRepository.GetByIdAsync(bankPayment.OrderId, cancellationToken);
        if (order == null)
            throw new BusinessException("Pedido asociado no encontrado");
        _branchContext.EnsureAccess(order.BranchId);
        _branchContext.EnsureAccess(bankPayment.Bank.BranchId);

        if (!_businessRules.CanModifyPayments(order, _currentUser.Role))
            throw new BusinessException("No tienes permisos para eliminar pagos de este pedido");

        if (!Roles.IsSuperadmin(_currentUser.Role) && order.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para eliminar este pago");

        if (!Roles.IsSuperadmin(_currentUser.Role) && bankPayment.Bank.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para eliminar este pago");

        if (bankPayment.Bank.BranchId != order.BranchId)
            throw new BusinessException("Inconsistencia entre pedido y banco");

        return await _bankPaymentRepository.DeleteAsync(request.Id, cancellationToken);
    }
}
