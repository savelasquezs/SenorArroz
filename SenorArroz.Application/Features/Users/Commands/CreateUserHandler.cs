// SenorArroz.Application/Features/Users/Commands/CreateUserHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.Users.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Domain.Interfaces.Services;

namespace SenorArroz.Application.Features.Users.Commands;

public class CreateUserHandler : IRequestHandler<CreateUserCommand, UserDto>
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordService _passwordService;
    private readonly IMapper _mapper;

    public CreateUserHandler(
        IUserRepository userRepository,
        IPasswordService passwordService,
        IMapper mapper)
    {
        _userRepository = userRepository;
        _passwordService = passwordService;
        _mapper = mapper;
    }

    public async Task<UserDto> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        // 1. Validar que el email no exista
        if (await _userRepository.EmailExistsAsync(request.UserData.Email, cancellationToken: cancellationToken))
        {
            throw new BusinessException($"Ya existe un usuario con el email '{request.UserData.Email}'");
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

