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
            .ForMember(dest => dest.BranchName, opt => opt.MapFrom(src => src.Branch.Name));

        CreateMap<CreateDeliverymanAdvanceDto, DeliverymanAdvance>();
        CreateMap<UpdateDeliverymanAdvanceDto, DeliverymanAdvance>();
    }
}

