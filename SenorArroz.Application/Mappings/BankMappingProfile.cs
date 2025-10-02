// SenorArroz.Application/Mappings/BankMappingProfile.cs
using AutoMapper;
using SenorArroz.Application.Features.Banks.DTOs;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Mappings;

public class BankMappingProfile : Profile
{
    public BankMappingProfile()
    {
        CreateMap<Bank, BankDto>()
            .ForMember(dest => dest.BranchName, opt => opt.MapFrom(src => src.Branch.Name))
            .ForMember(dest => dest.TotalApps, opt => opt.Ignore())
            .ForMember(dest => dest.ActiveApps, opt => opt.Ignore())
            .ForMember(dest => dest.CurrentBalance, opt => opt.Ignore());

        CreateMap<Bank, BankDetailDto>()
            .ForMember(dest => dest.BranchName, opt => opt.MapFrom(src => src.Branch.Name))
            .ForMember(dest => dest.TotalApps, opt => opt.Ignore())
            .ForMember(dest => dest.ActiveApps, opt => opt.Ignore())
            .ForMember(dest => dest.TotalBankPayments, opt => opt.Ignore())
            .ForMember(dest => dest.TotalExpenseBankPayments, opt => opt.Ignore())
            .ForMember(dest => dest.CurrentBalance, opt => opt.Ignore());

        CreateMap<CreateBankDto, Bank>();
        CreateMap<UpdateBankDto, Bank>();
    }
}
