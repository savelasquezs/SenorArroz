// SenorArroz.Application/Features/Users/Commands/CreateUserHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Users.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Domain.Interfaces.Services;

namespace SenorArroz.Application.Features.Users.Commands;

public class CreateUserHandler : IRequestHandler<CreateUserCommand, UserDto>
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordService _passwordService;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public CreateUserHandler(
        IUserRepository userRepository,
        IPasswordService passwordService,
        IMapper mapper,
        ICurrentUser currentUser)
    {
        _userRepository = userRepository;
        _passwordService = passwordService;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<UserDto> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {

        string creatorRole = _currentUser.Role;
        int creatorBranchId= _currentUser.BranchId;
       
        // 1. Validar que el email no exista
        if (await _userRepository.EmailExistsAsync(request.UserData.Email, cancellationToken: cancellationToken))
        {
            throw new BusinessException($"Ya existe un usuario con el email '{request.UserData.Email}'");
        }

        if (creatorRole=="admin")
        {
            // Solo puede crear usuarios de su sucursal
            if (creatorBranchId == 0)
            {
                throw new BusinessException("Un administrador debe estar asociado a una sucursal.");
            }
            if (creatorBranchId != request.UserData.BranchId)
            {
                throw new BusinessException("Un administrador solo puede crear usuarios de su sucursal");
            }

            // Forzamos la sucursal para evitar fraude
            request.UserData.BranchId = creatorBranchId;

            // Admin NO puede crear admin ni superadmin
            if (request.UserData.Role==UserRole.Admin ||
                request.UserData.Role==UserRole.Superadmin)
            {
                throw new BusinessException("Un administrador no puede crear usuarios con rol Admin o Superadmin.");
            }
        }
        
        else
        {
            // Otros roles no pueden crear usuarios
            throw new BusinessException("No tienes permisos para crear usuarios.");
        }


        // 2. Mapear DTO a entidad
        var user = _mapper.Map<User>(request.UserData);

        // 3. Hashear la contraseña
        user.PasswordHash = _passwordService.HashPassword(request.UserData.Password);

        // 4. Guardar en la base de datos
        var createdUser = await _userRepository.AddAsync(user, cancellationToken);

        // 5. Mapear entidad a DTO para la respuesta
        return _mapper.Map<UserDto>(createdUser);
    }
}

