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

        CreateMap<CreateSupplierDto, CreateSupplierCommand>();
        CreateMap<UpdateSupplierDto, UpdateSupplierCommand>()
            .ForMember(dest => dest.Id, opt => opt.Ignore());
    }
}


