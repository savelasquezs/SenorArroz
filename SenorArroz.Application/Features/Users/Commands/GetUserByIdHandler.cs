
using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.Users.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Application.Features.Users.Queries;

namespace SenorArroz.Application.Features.Users.Commands;

public class GetUserByIdHandler : IRequestHandler<GetUserByIdQuery, UserDto>
{
    private readonly IUserRepository _userRepository;
    private readonly IMapper _mapper;

    public GetUserByIdHandler(IUserRepository userRepository, IMapper mapper)
    {
        _userRepository = userRepository;
        _mapper = mapper;
    }

    public async Task<UserDto> Handle(GetUserByIdQuery request, CancellationToken cancellationToken)
    {
        // Buscar usuario por ID
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);

        // Si no existe, lanzar excepción
        if (user == null)
        {
            throw new NotFoundException($"Usuario con ID {request.UserId} no encontrado");
        }

        // Mapear a DTO
        return _mapper.Map<UserDto>(user);
    }
}

