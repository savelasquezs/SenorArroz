// SenorArroz.Application/Features/BankPayments/Commands/CreateBankPaymentHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.BankPayments.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.BankPayments.Commands;

public class CreateBankPaymentHandler : IRequestHandler<CreateBankPaymentCommand, BankPaymentDto>
{
    private readonly IBankPaymentRepository _bankPaymentRepository;
    private readonly IBankRepository _bankRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IOrderBusinessRulesService _businessRules;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public CreateBankPaymentHandler(
        IBankPaymentRepository bankPaymentRepository,
        IBankRepository bankRepository,
        IOrderRepository orderRepository,
        IOrderBusinessRulesService businessRules,
        IMapper mapper,
        ICurrentUser currentUser)
    {
        _bankPaymentRepository = bankPaymentRepository;
        _bankRepository = bankRepository;
        _orderRepository = orderRepository;
        _businessRules = businessRules;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<BankPaymentDto> Handle(CreateBankPaymentCommand request, CancellationToken cancellationToken)
    {
        var bank = await _bankRepository.GetByIdAsync(request.BankId, cancellationToken);
        if (bank == null)
            throw new BusinessException("El banco especificado no existe");

        if (!Roles.IsSuperadmin(_currentUser.Role) && bank.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para crear pagos en este banco");

        var order = await _orderRepository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order == null)
            throw new BusinessException("El pedido especificado no existe");

        if (!Roles.IsSuperadmin(_currentUser.Role) && order.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para modificar pagos de este pedido");

        if (bank.BranchId != order.BranchId)
            throw new BusinessException("El banco no pertenece a la sucursal del pedido");

        if (!_businessRules.CanModifyPayments(order, _currentUser.Role))
            throw new BusinessException("No tienes permisos para modificar pagos de este pedido");

        var bankPayment = new BankPayment
        {
            OrderId = request.OrderId,
            BankId = request.BankId,
            Amount = request.Amount
        };

        var createdBankPayment = await _bankPaymentRepository.CreateAsync(bankPayment, cancellationToken);
        return _mapper.Map<BankPaymentDto>(createdBankPayment);
    }
}
