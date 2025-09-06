// SenorArroz.Application/Features/Users/Commands/UpdateUserHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.Users.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Users.Commands;

public class UpdateUserHandler : IRequestHandler<UpdateUserCommand, UserDto>
{
    private readonly IUserRepository _userRepository;
    private readonly IMapper _mapper;

    public UpdateUserHandler(IUserRepository userRepository, IMapper mapper)
    {
        _userRepository = userRepository;
        _mapper = mapper;
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

        // 3. Mapear los cambios al usuario existente
        _mapper.Map(request.UserData, existingUser);

        // 4. Actualizar en la base de datos
        var updatedUser = await _userRepository.UpdateAsync(existingUser, cancellationToken);

        // 5. Retornar DTO
        return _mapper.Map<UserDto>(updatedUser);
    }
}

// Comando para actualizar usuario
public record UpdateUserCommand(int UserId, UpdateUserDto UserData) : IRequest<UserDto>;