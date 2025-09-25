using AutoMapper;
using SenorArroz.Application.Features.Branches.Commands;
using SenorArroz.Application.Features.Branches.DTOs;
using SenorArroz.Application.Features.Branches.Queries;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Mappings;

public class BranchMappingProfile : Profile
{
    public BranchMappingProfile()
    {
        // Branch -> BranchDto
        CreateMap<Branch, BranchDto>()
            .ForMember(dest => dest.TotalUsers, opt => opt.MapFrom(src => src.Users.Count))
            .ForMember(dest => dest.ActiveUsers, opt => opt.MapFrom(src => src.Users.Count(u => u.Active)))
            .ForMember(dest => dest.TotalCustomers, opt => opt.MapFrom(src => src.Customers.Count))
            .ForMember(dest => dest.ActiveCustomers, opt => opt.MapFrom(src => src.Customers.Count(c => c.Active)))
            .ForMember(dest => dest.TotalNeighborhoods, opt => opt.MapFrom(src => src.Neighborhoods.Count))
            .ForMember(dest => dest.Users, opt => opt.MapFrom(src => src.Users))
            .ForMember(dest => dest.Neighborhoods, opt => opt.MapFrom(src => src.Neighborhoods));

        // Neighborhood -> BranchNeighborhoodDto
        CreateMap<Neighborhood, BranchNeighborhoodDto>();

        // User -> BranchUserDto
        CreateMap<User, BranchUserDto>()
            .ForMember(dest => dest.Role, opt => opt.MapFrom(src => src.Role.ToString()))
            .ForMember(dest => dest.Active, opt => opt.MapFrom(src => src.Active));

        // Commands
        CreateMap<CreateBranchDto, CreateBranchCommand>();
        CreateMap<UpdateBranchDto, UpdateBranchCommand>()
            .ForMember(dest => dest.Id, opt => opt.Ignore()); // El Id lo sacamos de la ruta

        // Search / Query
        CreateMap<BranchSearchDto, GetBranchesQuery>();
    }
}
