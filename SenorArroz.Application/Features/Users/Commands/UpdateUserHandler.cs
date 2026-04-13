// SenorArroz.Application/Features/Users/Commands/UpdateUserHandler.cs
using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Users;
using SenorArroz.Application.Features.Users.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Users.Commands
{
    public class UpdateUserHandler : IRequestHandler<UpdateUserCommand, UserDto>
    {
        private readonly IUserRepository _userRepository;
        private readonly IMapper _mapper;
        private readonly ICurrentUser _currentUser;
        private readonly IApplicationDbContext _context;

        public UpdateUserHandler(
            IUserRepository userRepository,
            IMapper mapper,
            ICurrentUser currentUser,
            IApplicationDbContext context)
        {
            _userRepository = userRepository;
            _mapper = mapper;
            _currentUser = currentUser;
            _context = context;
        }

        public async Task<UserDto> Handle(UpdateUserCommand request, CancellationToken cancellationToken)
        {
            // 1. Verificar que el usuario existe
            var existingUser = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
            if (existingUser == null)
            {
                throw new NotFoundException($"Usuario con ID {request.UserId} no encontrado");
            }

            // 2. Verificar que el email no esté en uso por otro usuario
            if (await _userRepository.EmailExistsAsync(request.UserData.Email, request.UserId, cancellationToken))
            {
                throw new BusinessException($"Ya existe otro usuario con el email '{request.UserData.Email}'");
            }

            await UserPayrollExpenseRules.ValidatePayrollExpenseAssignmentAsync(
                request.UserData.PayrollExpenseId,
                request.UserId,
                _context,
                cancellationToken);

            // 3. Mapear los cambios al usuario existente (BranchId se ignora en el perfil de AutoMapper)
            _mapper.Map(request.UserData, existingUser);

            // 4. Cambio de sucursal: solo superadmin
            if (request.UserData.BranchId.HasValue)
            {
                if (!Roles.IsSuperadmin(_currentUser.Role))
                    throw new BusinessException("Solo el superadministrador puede cambiar la sucursal de un usuario");

                var newBranchId = request.UserData.BranchId.Value;
                if (newBranchId <= 0)
                    throw new BusinessException("Sucursal inválida");

                var branchExists = await _context.Branches.AnyAsync(b => b.Id == newBranchId, cancellationToken);
                if (!branchExists)
                    throw new NotFoundException($"Sucursal con ID {newBranchId} no encontrada");

                existingUser.BranchId = newBranchId;
            }

            // 5. Actualizar en la base de datos
            await _userRepository.UpdateAsync(existingUser, cancellationToken);

            var reloaded = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
            return _mapper.Map<UserDto>(reloaded!);
        }
    }

    // Comando para actualizar usuario
    public record UpdateUserCommand(int UserId, UpdateUserDto UserData) : IRequest<UserDto>;
}