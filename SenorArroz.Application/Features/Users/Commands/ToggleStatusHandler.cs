using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.Users.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Users.Commands;

public class ToggleStatusHandler : IRequestHandler<ToggleStatusCommand, UserDto>
{
    private readonly IUserRepository _userRepository;
    private readonly IMapper _mapper;

    public ToggleStatusHandler(IUserRepository userRepository, IMapper mapper)
    {
        _userRepository = userRepository;
        _mapper = mapper;
    }

    public async Task<UserDto> Handle(ToggleStatusCommand request, CancellationToken cancellationToken)
    {
        var existingUser = await _userRepository.GetByIdAsync(request.Id, cancellationToken);
        if (existingUser == null)
            throw new NotFoundException($"Usuario con ID {request.Id} no encontrado");

        // Invertir estado
        existingUser.Active = !existingUser.Active;

        var updatedUser = await _userRepository.UpdateAsync(existingUser, cancellationToken);

        return _mapper.Map<UserDto>(updatedUser);
    }
}
