using AutoMapper;
using SenorArroz.Application.Features.Suppliers.Commands;
using SenorArroz.Application.Features.Suppliers.DTOs;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Mappings;

public class SupplierMappingProfile : Profile
{
    public SupplierMappingProfile()
    {
        CreateMap<Supplier, SupplierDto>()
            .ForMember(dest => dest.BranchName, opt => opt.MapFrom(src => src.Branch.Name));

        CreateMap<CreateSupplierDto, CreateSupplierCommand>()
            .ForMember(dest => dest.Supplier, opt => opt.Ignore())
            .ForMember(dest => dest.BranchId, opt => opt.Ignore());

        CreateMap<UpdateSupplierDto, UpdateSupplierCommand>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.Supplier, opt => opt.Ignore());
    }
}


