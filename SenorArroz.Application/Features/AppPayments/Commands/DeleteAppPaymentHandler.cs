// SenorArroz.Application/Features/AppPayments/Commands/DeleteAppPaymentHandler.cs
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.AppPayments.Commands;

public class DeleteAppPaymentHandler : IRequestHandler<DeleteAppPaymentCommand, bool>
{
    private readonly IAppPaymentRepository _appPaymentRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IOrderBusinessRulesService _businessRules;
    private readonly ICurrentUser _currentUser;
    private readonly IBranchContext _branchContext;

    public DeleteAppPaymentHandler(
        IAppPaymentRepository appPaymentRepository,
        IOrderRepository orderRepository,
        IOrderBusinessRulesService businessRules,
        ICurrentUser currentUser,
        IBranchContext branchContext)
    {
        _appPaymentRepository = appPaymentRepository;
        _orderRepository = orderRepository;
        _businessRules = businessRules;
        _currentUser = currentUser;
        _branchContext = branchContext;
    }

    public async Task<bool> Handle(DeleteAppPaymentCommand request, CancellationToken cancellationToken)
    {
        var appPayment = await _appPaymentRepository.GetByIdAsync(request.Id, cancellationToken);
        if (appPayment == null)
            return false;

        var order = await _orderRepository.GetByIdAsync(appPayment.OrderId, cancellationToken);
        if (order == null)
            throw new BusinessException("Pedido asociado no encontrado");
        _branchContext.EnsureAccess(order.BranchId);
        _branchContext.EnsureAccess(appPayment.App.Bank.BranchId);

        if (!_businessRules.CanModifyPayments(order, _currentUser.Role))
            throw new BusinessException("No tienes permisos para eliminar pagos de este pedido");

        if (!Roles.IsSuperadmin(_currentUser.Role) && order.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para eliminar este pago");

        if (!Roles.IsSuperadmin(_currentUser.Role) && appPayment.App.Bank.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para eliminar este pago");

        if (appPayment.App.Bank.BranchId != order.BranchId)
            throw new BusinessException("Inconsistencia entre pedido y app");

        return await _appPaymentRepository.DeleteAsync(request.Id, cancellationToken);
    }
}
