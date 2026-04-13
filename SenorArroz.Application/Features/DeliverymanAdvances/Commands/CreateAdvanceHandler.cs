using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.DeliverymanAdvances.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.DeliverymanAdvances.Commands;

public class CreateAdvanceHandler : IRequestHandler<CreateAdvanceCommand, DeliverymanAdvanceDto>
{
    private readonly IDeliverymanAdvanceRepository _advanceRepository;
    private readonly IUserRepository _userRepository;
    private readonly IBankRepository _bankRepository;
    private readonly IExpenseHeaderRepository _expenseHeaderRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public CreateAdvanceHandler(
        IDeliverymanAdvanceRepository advanceRepository,
        IUserRepository userRepository,
        IBankRepository bankRepository,
        IExpenseHeaderRepository expenseHeaderRepository,
        IMapper mapper,
        ICurrentUser currentUser)
    {
        _advanceRepository = advanceRepository;
        _userRepository = userRepository;
        _bankRepository = bankRepository;
        _expenseHeaderRepository = expenseHeaderRepository;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<DeliverymanAdvanceDto> Handle(CreateAdvanceCommand request, CancellationToken cancellationToken)
    {
        var deliveryman = await _userRepository.GetByIdAsync(request.Advance.DeliverymanId, cancellationToken);
        if (deliveryman == null)
            throw new BusinessException("El domiciliario no existe");

        if (deliveryman.Role != UserRole.Deliveryman)
            throw new BusinessException("El usuario especificado no es un domiciliario");

        if (!deliveryman.Active)
            throw new BusinessException("El domiciliario no está activo");

        if (!Roles.IsSuperadmin(_currentUser.Role) && deliveryman.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para crear abonos en esta sucursal");

        if (request.Advance.Amount <= 0)
            throw new BusinessException("El monto debe ser mayor a cero");

        var method = request.Advance.PaymentMethod;

        if (method == DeliverymanAdvancePaymentMethod.Cash)
        {
            if (request.Advance.BankId.HasValue || request.Advance.ExpenseHeaderId.HasValue)
                throw new BusinessException("Abono en efectivo no debe incluir banco ni gasto");
        }
        else if (method == DeliverymanAdvancePaymentMethod.BankTransfer)
        {
            if (!request.Advance.BankId.HasValue)
                throw new BusinessException("La transferencia requiere banco");
            if (request.Advance.ExpenseHeaderId.HasValue)
                throw new BusinessException("La transferencia no debe incluir gasto vinculado");

            var bank = await _bankRepository.GetByIdAsync(request.Advance.BankId.Value, cancellationToken);
            if (bank == null || bank.BranchId != deliveryman.BranchId)
                throw new BusinessException("Banco inválido para esta sucursal");
        }
        else if (method == DeliverymanAdvancePaymentMethod.ExpenseOffset)
        {
            if (!request.Advance.ExpenseHeaderId.HasValue)
                throw new BusinessException("El abono por gasto requiere expenseHeaderId");
            if (request.Advance.BankId.HasValue)
                throw new BusinessException("El abono por gasto no debe incluir banco");

            var expense = await _expenseHeaderRepository.GetByIdWithDetailsAsync(request.Advance.ExpenseHeaderId.Value, cancellationToken);
            if (expense == null || expense.BranchId != deliveryman.BranchId)
                throw new BusinessException("Gasto no encontrado en esta sucursal");
            if (expense.DeliverymanId != deliveryman.Id)
                throw new BusinessException("El gasto no está asociado a este domiciliario");
            var expenseTotal = expense.Total ?? 0;
            if (Math.Abs(expenseTotal - request.Advance.Amount) > 0.02m)
                throw new BusinessException("El monto del abono debe coincidir con el total del gasto");

            if (await _advanceRepository.ExistsExpenseOffsetForExpenseHeaderAsync(
                    request.Advance.DeliverymanId,
                    request.Advance.ExpenseHeaderId.Value,
                    cancellationToken))
                throw new BusinessException("Ya existe un abono vinculado a este gasto para este domiciliario.");
        }

        var advance = new DeliverymanAdvance
        {
            DeliverymanId = request.Advance.DeliverymanId,
            Amount = request.Advance.Amount,
            PaymentMethod = method,
            BankId = method == DeliverymanAdvancePaymentMethod.BankTransfer ? request.Advance.BankId : null,
            ExpenseHeaderId = method == DeliverymanAdvancePaymentMethod.ExpenseOffset ? request.Advance.ExpenseHeaderId : null,
            Notes = request.Advance.Notes,
            CreatedBy = _currentUser.Id,
            BranchId = deliveryman.BranchId
        };

        var created = await _advanceRepository.CreateAsync(advance, cancellationToken);
        return _mapper.Map<DeliverymanAdvanceDto>(created);
    }
}
