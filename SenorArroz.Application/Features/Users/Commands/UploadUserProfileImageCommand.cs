using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Users.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Users.Commands;

public sealed record UploadUserProfileImageCommand(int UserId, byte[] FileBytes, string Extension)
    : IRequest<UserDto>;

public sealed class UploadUserProfileImageHandler : IRequestHandler<UploadUserProfileImageCommand, UserDto>
{
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp"];
    private const int MaxFileSizeBytes = 3 * 1024 * 1024;

    private readonly IUserProfileImageStorage _storage;
    private readonly IUserRepository _userRepository;

    public UploadUserProfileImageHandler(
        IUserProfileImageStorage storage,
        IUserRepository userRepository)
    {
        _storage = storage;
        _userRepository = userRepository;
    }

    public async Task<UserDto> Handle(UploadUserProfileImageCommand request, CancellationToken cancellationToken)
    {
        if (!AllowedExtensions.Contains(request.Extension))
            throw new BusinessException("Formato no permitido. Use jpg, png o webp.");

        if (request.FileBytes.Length > MaxFileSizeBytes)
            throw new BusinessException("La imagen no puede superar 3 MB.");

        var profileImageUrl = await _storage.SaveAndReplaceAsync(
            request.UserId,
            request.FileBytes,
            request.Extension,
            cancellationToken);

        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken)
            ?? throw new NotFoundException($"Usuario con ID {request.UserId} no encontrado");

        user.ProfileImageUrl = profileImageUrl;
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
