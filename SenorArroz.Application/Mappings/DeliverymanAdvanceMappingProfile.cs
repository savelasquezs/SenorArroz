using AutoMapper;
using SenorArroz.Application.Features.DeliverymanAdvances.DTOs;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Mappings;

public class DeliverymanAdvanceMappingProfile : Profile
{
    public DeliverymanAdvanceMappingProfile()
    {
        CreateMap<DeliverymanAdvance, DeliverymanAdvanceDto>()
            .ForMember(dest => dest.DeliverymanName, opt => opt.MapFrom(src => src.Deliveryman.Name))
            .ForMember(dest => dest.CreatedByName, opt => opt.MapFrom(src => src.Creator.Name))
            .ForMember(dest => dest.BranchName, opt => opt.MapFrom(src => src.Branch.Name))
            .ForMember(dest => dest.BankName, opt => opt.MapFrom(src => src.Bank != null ? src.Bank.Name : null));

        CreateMap<CreateDeliverymanAdvanceDto, DeliverymanAdvance>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
            .ForMember(dest => dest.BranchId, opt => opt.Ignore())
            .ForMember(dest => dest.Deliveryman, opt => opt.Ignore())
            .ForMember(dest => dest.Creator, opt => opt.Ignore())
            .ForMember(dest => dest.Branch, opt => opt.Ignore())
            .ForMember(dest => dest.Bank, opt => opt.Ignore())
            .ForMember(dest => dest.ExpenseHeader, opt => opt.Ignore());
        CreateMap<UpdateDeliverymanAdvanceDto, DeliverymanAdvance>()
            .ForMember(dest => dest.DeliverymanId, opt => opt.Ignore())
            .ForMember(dest => dest.PaymentMethod, opt => opt.Ignore())
            .ForMember(dest => dest.BankId, opt => opt.Ignore())
            .ForMember(dest => dest.ExpenseHeaderId, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
            .ForMember(dest => dest.BranchId, opt => opt.Ignore())
            .ForMember(dest => dest.Deliveryman, opt => opt.Ignore())
            .ForMember(dest => dest.Creator, opt => opt.Ignore())
            .ForMember(dest => dest.Branch, opt => opt.Ignore())
            .ForMember(dest => dest.Bank, opt => opt.Ignore())
            .ForMember(dest => dest.ExpenseHeader, opt => opt.Ignore())
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore());
    }
}
