// SenorArroz.Application/Features/Users/Commands/CreateUserHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Users;
using SenorArroz.Application.Features.Users.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Domain.Interfaces.Services;

namespace SenorArroz.Application.Features.Users.Commands
{
    public class CreateUserHandler : IRequestHandler<CreateUserCommand, UserDto>
    {
        private readonly IUserRepository _userRepository;
        private readonly IPasswordService _passwordService;
        private readonly IMapper _mapper;
        private readonly ICurrentUser _currentUser;
        private readonly IBranchContext _branchContext;
        private readonly IApplicationDbContext _db;

        public CreateUserHandler(
            IUserRepository userRepository,
            IPasswordService passwordService,
            IMapper mapper,
            ICurrentUser currentUser,
            IBranchContext branchContext,
            IApplicationDbContext db)
        {
            _userRepository = userRepository;
            _passwordService = passwordService;
            _mapper = mapper;
            _currentUser = currentUser;
            _branchContext = branchContext;
            _db = db;
        }

        public async Task<UserDto> Handle(CreateUserCommand request, CancellationToken cancellationToken)
        {

            string creatorRole = _currentUser.Role;
            int creatorBranchId= _currentUser.BranchId;
            request.UserData.BranchId = _branchContext.RequireBranch(request.UserData.BranchId);
       
            // 1. Validar que el email no exista
            if (await _userRepository.EmailExistsAsync(request.UserData.Email, cancellationToken: cancellationToken))
            {
                throw new BusinessException($"Ya existe un usuario con el email '{request.UserData.Email}'");
            }

            if (Roles.IsAdmin(creatorRole))
            {
                // Solo puede crear usuarios de su sucursal
                if (creatorBranchId == 0)
                    throw new BusinessException("Un administrador debe estar asociado a una sucursal.");

                if (creatorBranchId != request.UserData.BranchId)
                    throw new BusinessException("Un administrador solo puede crear usuarios de su sucursal");

                // Forzamos la sucursal para evitar fraude
                request.UserData.BranchId = creatorBranchId;

                // Admin NO puede crear admin ni superadmin
                if (request.UserData.Role == UserRole.Admin ||
                    request.UserData.Role == UserRole.Superadmin)
                {
                    throw new BusinessException("Un administrador no puede crear usuarios con rol Admin o Superadmin.");
                }
                if (request.UserData.Role == UserRole.Kitchen)
                {
                    bool kitchenExists = await _userRepository.KitchenExistsInBranchAsync(request.UserData.BranchId, cancellationToken);
                    if (kitchenExists)
                    {
                        throw new BusinessException("Solo puede existir un usuario con rol Cocina por sucursal.");
                    }
                }
            }
            else if (Roles.IsSuperadmin(creatorRole))
            {
                // Validar restricciones de rol
                if (request.UserData.Role == UserRole.Superadmin)
                {
                    bool superadminExists = await _userRepository.RoleExistsAsync(UserRole.Superadmin, cancellationToken);
                    if (superadminExists)
                    {
                        throw new BusinessException("Solo puede existir un Superadmin en la aplicación.");
                    }
                }

                if (request.UserData.Role == UserRole.Admin)
                {
                    bool adminExists = await _userRepository.AdminExistsInBranchAsync(request.UserData.BranchId, cancellationToken);
                    if (adminExists)
                    {
                        throw new BusinessException("Ya existe un Admin en esta sucursal.");
                    }
                }

                if (request.UserData.Role == UserRole.Kitchen)
                {
                    bool  kitchenExists = await _userRepository.KitchenExistsInBranchAsync(request.UserData.BranchId, cancellationToken);
                    if (kitchenExists)
                    {
                        throw new BusinessException("Solo puede existir un usuario con rol Cocina en la aplicación.");
                    }
                }
            }
            else
            {
                // Otros roles no pueden crear usuarios
                throw new BusinessException("No tienes permisos para crear usuarios.");
            }

            await UserPayrollExpenseRules.ValidatePayrollExpenseAssignmentAsync(
                request.UserData.PayrollExpenseId,
                excludeUserId: null,
                _db,
                cancellationToken);

            // 2. Mapear DTO a entidad
            var user = _mapper.Map<User>(request.UserData);

            // 3. Hashear la contraseña
            user.PasswordHash = _passwordService.HashPassword(request.UserData.Password);

            // 4. Guardar en la base de datos
            var createdUser = await _userRepository.AddAsync(user, cancellationToken);

            var reloaded = await _userRepository.GetByIdAsync(createdUser.Id, cancellationToken);
            return _mapper.Map<UserDto>(reloaded ?? createdUser);
        }
    }

}
