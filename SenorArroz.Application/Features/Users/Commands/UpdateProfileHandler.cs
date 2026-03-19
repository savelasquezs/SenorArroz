using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.Users.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Users.Commands
{
    public record UpdateProfileCommand(int UserId, UpdateProfileDto Data) : IRequest<UserDto>;

    public class UpdateProfileHandler : IRequestHandler<UpdateProfileCommand, UserDto>
    {
        private readonly IUserRepository _userRepository;
        private readonly IMapper _mapper;

        public UpdateProfileHandler(IUserRepository userRepository, IMapper mapper)
        {
            _userRepository = userRepository;
            _mapper = mapper;
        }

        public async Task<UserDto> Handle(UpdateProfileCommand request, CancellationToken cancellationToken)
        {
            var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken)
                ?? throw new NotFoundException($"Usuario con ID {request.UserId} no encontrado");

            if (await _userRepository.EmailExistsAsync(request.Data.Email, request.UserId, cancellationToken))
                throw new BusinessException($"Ya existe otro usuario con el email '{request.Data.Email}'");

            user.Email = request.Data.Email.Trim();
            user.Phone = request.Data.Phone.Trim();

            var updated = await _userRepository.UpdateAsync(user, cancellationToken);
            return _mapper.Map<UserDto>(updated);
        }
    }
}
