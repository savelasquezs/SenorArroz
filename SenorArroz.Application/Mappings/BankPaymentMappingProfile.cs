// SenorArroz.Application/Mappings/BankPaymentMappingProfile.cs
using AutoMapper;
using SenorArroz.Application.Features.BankPayments.DTOs;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Mappings;

public class BankPaymentMappingProfile : Profile
{
    public BankPaymentMappingProfile()
    {
        // Navegaciones opcionales: sin Include/ThenInclude no hay NRE; el cliente recibe cadenas vacías / 0.
        CreateMap<BankPayment, BankPaymentDto>()
            .ForMember(dest => dest.BankName, opt => opt.MapFrom(src => src.Bank != null ? src.Bank.Name : string.Empty))
            .ForMember(dest => dest.BranchId, opt => opt.MapFrom(src => src.Bank != null ? src.Bank.BranchId : 0))
            .ForMember(dest => dest.BranchName, opt => opt.MapFrom(src =>
                src.Bank != null && src.Bank.Branch != null ? src.Bank.Branch.Name : string.Empty))
            .ForMember(dest => dest.SourceReservationDepositId, opt => opt.MapFrom(src => src.SourceReservationDepositId));

        CreateMap<CreateBankPaymentDto, BankPayment>()
            .ForMember(dest => dest.SourceReservationDepositId, opt => opt.Ignore())
            .ForMember(dest => dest.IsAppSettlement, opt => opt.Ignore())
            .ForMember(dest => dest.AppSettlementSourcePaymentIds, opt => opt.Ignore())
            .ForMember(dest => dest.IsVerified, opt => opt.Ignore())
            .ForMember(dest => dest.VerifiedAt, opt => opt.Ignore())
            .ForMember(dest => dest.Order, opt => opt.Ignore())
            .ForMember(dest => dest.Bank, opt => opt.Ignore())
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore());

        CreateMap<VerifyBankPaymentDto, BankPayment>()
            .ForMember(dest => dest.VerifiedAt, opt => opt.MapFrom(src => src.VerifiedAt))
            .ForMember(dest => dest.OrderId, opt => opt.Ignore())
            .ForMember(dest => dest.BankId, opt => opt.Ignore())
            .ForMember(dest => dest.Amount, opt => opt.Ignore())
            .ForMember(dest => dest.SourceReservationDepositId, opt => opt.Ignore())
            .ForMember(dest => dest.IsAppSettlement, opt => opt.Ignore())
            .ForMember(dest => dest.AppSettlementSourcePaymentIds, opt => opt.Ignore())
            .ForMember(dest => dest.IsVerified, opt => opt.Ignore())
            .ForMember(dest => dest.Order, opt => opt.Ignore())
            .ForMember(dest => dest.Bank, opt => opt.Ignore())
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore());
    }
}
