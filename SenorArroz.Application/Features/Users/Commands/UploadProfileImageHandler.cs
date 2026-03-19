using MediatR;
using SenorArroz.Application.Features.Users.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Users.Commands
{
    public record UploadProfileImageCommand(int UserId, string ProfileImageUrl) : IRequest<UserDto>;

    public class UploadProfileImageHandler : IRequestHandler<UploadProfileImageCommand, UserDto>
    {
        private readonly IUserRepository _userRepository;

        public UploadProfileImageHandler(IUserRepository userRepository)
        {
            _userRepository = userRepository;
        }

        public async Task<UserDto> Handle(UploadProfileImageCommand request, CancellationToken cancellationToken)
        {
            var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken)
                ?? throw new NotFoundException($"Usuario con ID {request.UserId} no encontrado");

            user.ProfileImageUrl = request.ProfileImageUrl;
            var updated = await _userRepository.UpdateAsync(user, cancellationToken);

            return new UserDto
            {
                Id = updated.Id,
                Name = updated.Name,
                Email = updated.Email,
                Phone = updated.Phone,
                Role = updated.Role!.Value,
                BranchId = updated.BranchId,
                BranchName = updated.Branch?.Name ?? string.Empty,
                Active = updated.Active,
                ProfileImageUrl = updated.ProfileImageUrl,
                CreatedAt = updated.CreatedAt,
                UpdatedAt = updated.UpdatedAt,
            };
        }
    }
}
