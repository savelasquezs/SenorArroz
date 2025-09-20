// SenorArroz.Application/Mappings/UserMappingProfile.cs
using AutoMapper;
using SenorArroz.Application.Features.Users.DTOs;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Mappings
{
    public class UserMappingProfile : Profile
    {
        public UserMappingProfile()
        {
            // User -> UserDto (para responses)
            CreateMap<User, UserDto>()
                .ForMember(dest => dest.BranchName,
                           opt => opt.MapFrom(src => src.Branch != null ? src.Branch.Name : string.Empty));

            // CreateUserDto -> User (para crear)
            CreateMap<CreateUserDto, User>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.Branch, opt => opt.Ignore())
                .ForMember(dest => dest.PasswordHash, opt => opt.Ignore()) // Se asigna en el handler
                .ForMember(dest => dest.Active, opt => opt.MapFrom(src => true));

            // UpdateUserDto -> User (para actualizar)
            CreateMap<UpdateUserDto, User>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.Branch, opt => opt.Ignore())
                .ForMember(dest => dest.BranchId, opt => opt.Ignore()) // No se puede cambiar la sucursal
                .ForMember(dest => dest.PasswordHash, opt => opt.Ignore()); // Password se maneja por separado
        }
    }
}