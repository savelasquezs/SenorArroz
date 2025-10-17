// SenorArroz.Application/Mappings/BankPaymentMappingProfile.cs
using AutoMapper;
using SenorArroz.Application.Features.BankPayments.DTOs;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Mappings;

public class BankPaymentMappingProfile : Profile
{
    public BankPaymentMappingProfile()
    {
        CreateMap<BankPayment, BankPaymentDto>()
            .ForMember(dest => dest.BankName, opt => opt.MapFrom(src => src.Bank.Name))
            .ForMember(dest => dest.BranchId, opt => opt.MapFrom(src => src.Bank.BranchId))
            .ForMember(dest => dest.BranchName, opt => opt.MapFrom(src => src.Bank.Branch.Name));

        CreateMap<CreateBankPaymentDto, BankPayment>();
        CreateMap<VerifyBankPaymentDto, BankPayment>()
            .ForMember(dest => dest.VerifiedAt, opt => opt.MapFrom(src => src.VerifiedAt));
    }
}
