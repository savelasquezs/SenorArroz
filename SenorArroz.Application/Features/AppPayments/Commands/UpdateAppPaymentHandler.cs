using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.AppPayments.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.AppPayments.Commands;

public class UpdateAppPaymentHandler : IRequestHandler<UpdateAppPaymentCommand, AppPaymentDto>
{
    private readonly IAppPaymentRepository _appPaymentRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IOrderBusinessRulesService _businessRules;
    private readonly ICurrentUser _currentUser;
    private readonly IMapper _mapper;

    public UpdateAppPaymentHandler(
        IAppPaymentRepository appPaymentRepository,
        IOrderRepository orderRepository,
        IOrderBusinessRulesService businessRules,
        ICurrentUser currentUser,
        IMapper mapper)
    {
        _appPaymentRepository = appPaymentRepository;
        _orderRepository = orderRepository;
        _businessRules = businessRules;
        _currentUser = currentUser;
        _mapper = mapper;
    }

    public async Task<AppPaymentDto> Handle(UpdateAppPaymentCommand request, CancellationToken cancellationToken)
    {
        // Validar que el pago existe
        var appPayment = await _appPaymentRepository.GetByIdAsync(request.Id);
        if (appPayment == null)
            throw new BusinessException("Pago por app no encontrado");

        // Obtener el pedido asociado
        var order = await _orderRepository.GetByIdAsync(appPayment.OrderId);
        if (order == null)
            throw new BusinessException("Pedido asociado no encontrado");

        // Validar permisos para modificar pagos
        if (!_businessRules.CanModifyPayments(order, _currentUser.Role))
            throw new BusinessException("No tienes permisos para modificar pagos de este pedido");

        // Validar acceso a sucursal
        if (_currentUser.Role != "superadmin" && appPayment.App.Bank.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para modificar pagos de esta sucursal");

        // Actualizar el monto
        appPayment.Amount = request.Amount;
        
        var updatedPayment = await _appPaymentRepository.UpdateAsync(appPayment);
        return _mapper.Map<AppPaymentDto>(updatedPayment);
    }
}

