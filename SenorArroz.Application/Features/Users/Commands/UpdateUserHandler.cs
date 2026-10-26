// SenorArroz.Application/Features/Users/Commands/UpdateUserHandler.cs
using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Users;
using SenorArroz.Application.Features.Users.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Users.Commands
{
    public class UpdateUserHandler : IRequestHandler<UpdateUserCommand, UserDto>
    {
        private readonly IUserRepository _userRepository;
        private readonly IMapper _mapper;
        private readonly ICurrentUser _currentUser;
        private readonly IApplicationDbContext _context;
        private readonly IDeliveryAutoCompletionRouteLock _routeLock;

        public UpdateUserHandler(IUserRepository userRepository, IMapper mapper,
            ICurrentUser currentUser, IApplicationDbContext context, IDeliveryAutoCompletionRouteLock routeLock)
        {
            _userRepository = userRepository;
            _mapper = mapper;
            _currentUser = currentUser;
            _context = context;
            _routeLock = routeLock;
        }

        public async Task<UserDto> Handle(UpdateUserCommand request, CancellationToken cancellationToken)
        {
            UserDto result = null!;
            await _routeLock.ExecuteDriverAsync(request.UserId, async ct => result = await UpdateAsync(request, ct), cancellationToken);
            return result;
        }

        private async Task<UserDto> UpdateAsync(UpdateUserCommand request, CancellationToken cancellationToken)
        {
            var existingUser = await _userRepository.GetByIdAsync(request.UserId, cancellationToken)
                ?? throw new NotFoundException($"Usuario con ID {request.UserId} no encontrado");
            if (!Roles.IsAdminOrSuperadmin(_currentUser.Role)) throw new UnauthorizedAccessException();
            if (Roles.IsAdmin(_currentUser.Role) && (existingUser.BranchId != _currentUser.BranchId
                || existingUser.Role is UserRole.Admin or UserRole.Superadmin
                || request.UserData.Role is UserRole.Admin or UserRole.Superadmin))
                throw new UnauthorizedAccessException();
            if (existingUser.IsTerminal && (request.UserData.Role != UserRole.Kitchen
                || request.UserData.PayrollExpenseId.HasValue || request.UserData.DeliveryAppEnabled == true))
                throw new BusinessException("Una terminal no puede convertirse en empleado ni recibir nómina o acceso a domicilios.");
            if (await _userRepository.EmailExistsAsync(request.UserData.Email, request.UserId, cancellationToken))
                throw new BusinessException($"Ya existe otro usuario con el email '{request.UserData.Email}'");
            await UserPayrollExpenseRules.ValidatePayrollExpenseAssignmentAsync(
                request.UserData.PayrollExpenseId, request.UserId, _context, cancellationToken);

            var previousRole = existingUser.Role;
            var previousDeliveryAppEnabled = existingUser.DeliveryAppEnabled;
            var previousBranchId = existingUser.BranchId;
            if (request.UserData.BranchId is int targetBranch && targetBranch != previousBranchId
                && (await _context.DeliveryRoutes.AnyAsync(r => r.DeliverymanId == existingUser.Id
                    && (r.Status == DeliveryRouteStatus.Open || r.Status == DeliveryRouteStatus.InProgress), cancellationToken)
                    || await _context.Orders.AnyAsync(o => o.DeliveryManId == existingUser.Id
                        && o.Status != OrderStatus.Delivered && o.Status != OrderStatus.Cancelled, cancellationToken)))
                throw new BusinessException("Finaliza la ruta activa o desasigna los pedidos antes de cambiar la sucursal del usuario.");
            _mapper.Map(request.UserData, existingUser); // AccountType es inmutable y está excluido del mapping.
            if (request.UserData.DeliveryAppEnabled.HasValue)
                existingUser.DeliveryAppEnabled = request.UserData.DeliveryAppEnabled.Value;
            if (existingUser.Role is not (UserRole.Admin or UserRole.Cashier))
            {
                if (request.UserData.DeliveryAppEnabled == true)
                    throw new BusinessException("Solo administradores y cajeros pueden usar la app de domicilios como rol adicional.");
                existingUser.DeliveryAppEnabled = false;
            }
            if (existingUser.Role == UserRole.Kitchen)
                existingUser.WebAccessEnabled = request.UserData.WebAccessEnabled ?? existingUser.WebAccessEnabled;
            else if (existingUser.Role != UserRole.Deliveryman)
                existingUser.WebAccessEnabled = true;
            else if (previousRole != UserRole.Deliveryman)
                existingUser.WebAccessEnabled = false;

            if (request.UserData.BranchId.HasValue)
            {
                if (!Roles.IsSuperadmin(_currentUser.Role))
                    throw new BusinessException("Solo el superadministrador puede cambiar la sucursal de un usuario");
                var newBranchId = request.UserData.BranchId.Value;
                if (newBranchId <= 0) throw new BusinessException("Sucursal inválida");
                var newBranch = await _context.Branches.AsNoTracking().FirstOrDefaultAsync(
                    branch => branch.Id == newBranchId, cancellationToken)
                    ?? throw new NotFoundException($"Sucursal con ID {newBranchId} no encontrada");
                if (newBranch.TenantId != existingUser.TenantId)
                    throw new BusinessException("No se puede asignar un usuario a una sucursal de otro tenant.");
                existingUser.BranchId = newBranchId;
            }
            if (previousRole != existingUser.Role || previousDeliveryAppEnabled != existingUser.DeliveryAppEnabled
                || previousBranchId != existingUser.BranchId) existingUser.ActiveSessionId = null;
            await _userRepository.UpdateAsync(existingUser, cancellationToken);
            return _mapper.Map<UserDto>((await _userRepository.GetByIdAsync(request.UserId, cancellationToken))!);
        }
    }

    public record UpdateUserCommand(int UserId, UpdateUserDto UserData) : IRequest<UserDto>;
}
