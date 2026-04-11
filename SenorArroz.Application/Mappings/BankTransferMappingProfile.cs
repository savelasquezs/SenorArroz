using AutoMapper;
using SenorArroz.Application.Features.BankTransfers.DTOs;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Mappings;

public class BankTransferMappingProfile : Profile
{
    public BankTransferMappingProfile()
    {
        CreateMap<BankTransfer, BankTransferDto>()
            .ForMember(dest => dest.FromBankName, opt => opt.MapFrom(src => src.FromBankId == null ? "Efectivo (caja)" : src.FromBank!.Name))
            .ForMember(dest => dest.ToBankName, opt => opt.MapFrom(src => src.ToBankId == null ? "Efectivo (caja)" : src.ToBank!.Name))
            .ForMember(dest => dest.CreatedByName, opt => opt.MapFrom(src => src.CreatedBy.Name ?? src.CreatedBy.Email));
    }
}
