// SenorArroz.Application/Mappings/AppPaymentMappingProfile.cs
using AutoMapper;
using SenorArroz.Application.Features.AppPayments.DTOs;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Mappings;

public class AppPaymentMappingProfile : Profile
{
    public AppPaymentMappingProfile()
    {
        CreateMap<AppPayment, AppPaymentDto>()
            .ForMember(dest => dest.AppName, opt => opt.MapFrom(src => src.App.Name))
            .ForMember(dest => dest.BankId, opt => opt.MapFrom(src => src.App.BankId))
            .ForMember(dest => dest.BankName, opt => opt.MapFrom(src => src.App.Bank.Name))
            .ForMember(dest => dest.BranchId, opt => opt.MapFrom(src => src.App.Bank.BranchId))
            .ForMember(dest => dest.BranchName, opt => opt.MapFrom(src => src.App.Bank.Branch.Name));

        CreateMap<CreateAppPaymentDto, AppPayment>();
    }
}
