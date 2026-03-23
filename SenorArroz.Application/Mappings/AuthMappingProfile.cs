// SenorArroz.Application/Mappings/AuthMappingProfile.cs
using AutoMapper;
using SenorArroz.Application.Features.Auth.Commands;
using SenorArroz.Application.Features.Auth.DTOs;
using SenorArroz.Application.Features.Auth.Queries;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Mappings
{
    public class AuthMappingProfile : Profile
    {
        public AuthMappingProfile()
        {
            CreateMap<User, UserInfoDto>()
                .ForMember(dest => dest.BranchName, opt => opt.MapFrom(src => src.Branch.Name))
                .ForMember(dest => dest.BranchAddress, opt => opt.MapFrom(src => src.Branch.Address))
                .ForMember(dest => dest.BranchPhone1, opt => opt.MapFrom(src => src.Branch.Phone1))
                .ForMember(dest => dest.BranchPhone2, opt => opt.MapFrom(src => src.Branch.Phone2))
                .ForMember(dest => dest.BranchLatitude, opt => opt.MapFrom(src => src.Branch.Latitude))
                .ForMember(dest => dest.BranchLongitude, opt => opt.MapFrom(src => src.Branch.Longitude));

            CreateMap<LoginDto, LoginCommand>();
            CreateMap<RefreshTokenDto, RefreshTokenCommand>()
                .ForMember(dest => dest.Token, opt => opt.Ignore())
                .ForMember(dest => dest.IpAddress, opt => opt.Ignore());
            CreateMap<ChangePasswordDto, ChangePasswordCommand>()
                .ForMember(dest => dest.UserId, opt => opt.Ignore());
            CreateMap<ForgotPasswordDto, ForgotPasswordCommand>()
                .ForMember(dest => dest.ResetUrl, opt => opt.Ignore());

            CreateMap<ResetPasswordDto, ResetPasswordCommand>()
                .ForMember(dest => dest.IpAddress, opt => opt.Ignore());

            CreateMap<ValidateResetTokenDto, ValidateResetTokenQuery>();
        }
    }
}