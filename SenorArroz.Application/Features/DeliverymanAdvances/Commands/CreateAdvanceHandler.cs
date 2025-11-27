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
    private readonly IOrderRepository _orderRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public CreateAdvanceHandler(
        IDeliverymanAdvanceRepository advanceRepository,
        IUserRepository userRepository,
        IOrderRepository orderRepository,
        IMapper mapper,
        ICurrentUser currentUser)
    {
        _advanceRepository = advanceRepository;
        _userRepository = userRepository;
        _orderRepository = orderRepository;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<DeliverymanAdvanceDto> Handle(CreateAdvanceCommand request, CancellationToken cancellationToken)
    {
        // 1. Validar que el deliveryman existe
        var deliveryman = await _userRepository.GetByIdAsync(request.Advance.DeliverymanId);
        if (deliveryman == null)
            throw new BusinessException("El domiciliario no existe");

        // 2. Validar que es rol Deliveryman
        if (deliveryman.Role != UserRole.Deliveryman)
            throw new BusinessException("El usuario especificado no es un domiciliario");

        // 3. Validar que está activo
        if (!deliveryman.Active)
            throw new BusinessException("El domiciliario no está activo");

        // 4. Validar acceso a sucursal
        if (_currentUser.Role != "superadmin" && deliveryman.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para crear abonos en esta sucursal");

        // 5. Validar monto > 0
        if (request.Advance.Amount <= 0)
            throw new BusinessException("El monto debe ser mayor a cero");

        // Crear el abono
        var advance = new DeliverymanAdvance
        {
            DeliverymanId = request.Advance.DeliverymanId,
            Amount = request.Advance.Amount,
            Notes = request.Advance.Notes,
            CreatedBy = _currentUser.Id,
            BranchId = deliveryman.BranchId
        };

        var created = await _advanceRepository.CreateAsync(advance);
        return _mapper.Map<DeliverymanAdvanceDto>(created);
    }
}

