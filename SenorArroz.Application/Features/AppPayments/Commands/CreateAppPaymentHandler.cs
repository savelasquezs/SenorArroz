// SenorArroz.Application/Features/AppPayments/Commands/CreateAppPaymentHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.AppPayments.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.AppPayments.Commands;

public class CreateAppPaymentHandler : IRequestHandler<CreateAppPaymentCommand, AppPaymentDto>
{
    private readonly IAppPaymentRepository _appPaymentRepository;
    private readonly IAppRepository _appRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IOrderBusinessRulesService _businessRules;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public CreateAppPaymentHandler(
        IAppPaymentRepository appPaymentRepository,
        IAppRepository appRepository,
        IOrderRepository orderRepository,
        IOrderBusinessRulesService businessRules,
        IMapper mapper,
        ICurrentUser currentUser)
    {
        _appPaymentRepository = appPaymentRepository;
        _appRepository = appRepository;
        _orderRepository = orderRepository;
        _businessRules = businessRules;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<AppPaymentDto> Handle(CreateAppPaymentCommand request, CancellationToken cancellationToken)
    {
        var app = await _appRepository.GetByIdAsync(request.AppId, cancellationToken);
        if (app == null)
            throw new BusinessException("La app especificada no existe");

        if (!Roles.IsSuperadmin(_currentUser.Role) && app.Bank.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para crear pagos en esta app");

        var order = await _orderRepository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order == null)
            throw new BusinessException("El pedido especificado no existe");

        if (!Roles.IsSuperadmin(_currentUser.Role) && order.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para modificar pagos de este pedido");

        if (app.Bank.BranchId != order.BranchId)
            throw new BusinessException("La app no pertenece a la sucursal del pedido");

        if (!_businessRules.CanModifyPayments(order, _currentUser.Role))
            throw new BusinessException("No tienes permisos para modificar pagos de este pedido");

        var existingForOrder = await _appPaymentRepository.GetByOrderIdAsync(request.OrderId, cancellationToken);
        if (existingForOrder.Any())
            throw new BusinessException("Solo se permite un pago por app por pedido");

        var appPayment = new AppPayment
        {
            OrderId = request.OrderId,
            AppId = request.AppId,
            Amount = request.Amount
        };

        var createdAppPayment = await _appPaymentRepository.CreateAsync(appPayment, cancellationToken);
        return _mapper.Map<AppPaymentDto>(createdAppPayment);
    }
}
