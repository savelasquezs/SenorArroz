using AutoMapper;
using MediatR;
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
    private readonly IMapper _mapper;

    public UpdateBankPaymentHandler(
        IBankPaymentRepository bankPaymentRepository,
        IOrderRepository orderRepository,
        IOrderBusinessRulesService businessRules,
        ICurrentUser currentUser,
        IMapper mapper)
    {
        _bankPaymentRepository = bankPaymentRepository;
        _orderRepository = orderRepository;
        _businessRules = businessRules;
        _currentUser = currentUser;
        _mapper = mapper;
    }

    public async Task<BankPaymentDto> Handle(UpdateBankPaymentCommand request, CancellationToken cancellationToken)
    {
        // Validar que el pago existe
        var bankPayment = await _bankPaymentRepository.GetByIdAsync(request.Id);
        if (bankPayment == null)
            throw new BusinessException("Pago bancario no encontrado");

        // Obtener el pedido asociado
        var order = await _orderRepository.GetByIdAsync(bankPayment.OrderId);
        if (order == null)
            throw new BusinessException("Pedido asociado no encontrado");

        // Validar permisos para modificar pagos
        if (!_businessRules.CanModifyPayments(order, _currentUser.Role))
            throw new BusinessException("No tienes permisos para modificar pagos de este pedido");

        // Validar acceso a sucursal
        if (_currentUser.Role != "superadmin" && bankPayment.Bank.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para modificar pagos de esta sucursal");

        // Actualizar el monto
        bankPayment.Amount = request.Amount;
        
        var updatedPayment = await _bankPaymentRepository.UpdateAsync(bankPayment);
        return _mapper.Map<BankPaymentDto>(updatedPayment);
    }
}

