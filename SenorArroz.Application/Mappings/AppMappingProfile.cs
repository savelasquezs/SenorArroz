// SenorArroz.Application/Mappings/AppMappingProfile.cs
using AutoMapper;
using SenorArroz.Application.Features.Apps.DTOs;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Mappings;

public class AppMappingProfile : Profile
{
    public AppMappingProfile()
    {
        CreateMap<App, AppDto>()
            .ForMember(dest => dest.BankName, opt => opt.MapFrom(src => src.Bank.Name))
            .ForMember(dest => dest.BranchId, opt => opt.MapFrom(src => src.Bank.BranchId))
            .ForMember(dest => dest.BranchName, opt => opt.MapFrom(src => src.Bank.Branch.Name))
            .ForMember(dest => dest.TotalPayments, opt => opt.Ignore())
            .ForMember(dest => dest.UnsettledPayments, opt => opt.Ignore())
            .ForMember(dest => dest.TotalPaymentsCount, opt => opt.Ignore())
            .ForMember(dest => dest.UnsettledPaymentsCount, opt => opt.Ignore());

        CreateMap<CreateAppDto, App>();
        CreateMap<UpdateAppDto, App>();
    }
}
