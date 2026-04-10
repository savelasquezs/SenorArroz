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
            .ForMember(dest => dest.AppName, opt => opt.MapFrom(src => src.App != null ? src.App.Name : string.Empty))
            .ForMember(dest => dest.BankId, opt => opt.MapFrom(src => src.App != null ? src.App.BankId : 0))
            .ForMember(dest => dest.BankName, opt => opt.MapFrom(src =>
                src.App != null && src.App.Bank != null ? src.App.Bank.Name : string.Empty))
            .ForMember(dest => dest.BranchId, opt => opt.MapFrom(src =>
                src.App != null && src.App.Bank != null ? src.App.Bank.BranchId : 0))
            .ForMember(dest => dest.BranchName, opt => opt.MapFrom(src =>
                src.App != null && src.App.Bank != null && src.App.Bank.Branch != null
                    ? src.App.Bank.Branch.Name
                    : string.Empty));

        CreateMap<CreateAppPaymentDto, AppPayment>();
    }
}
