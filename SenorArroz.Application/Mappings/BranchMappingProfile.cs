using AutoMapper;
using SenorArroz.Application.Features.Branches.Commands;
using SenorArroz.Application.Features.Branches.DTOs;
using SenorArroz.Application.Features.Branches.Queries;
using SenorArroz.Application.Features.BranchPrintSettings.DTOs;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Mappings;

public class BranchMappingProfile : Profile
{
    public BranchMappingProfile()
    {
        // Branch -> BranchDtodotn
        CreateMap<Branch, BranchDto>()
            .ForMember(dest => dest.TotalUsers, opt => opt.MapFrom(src => src.Users.Count))
            .ForMember(dest => dest.ActiveUsers, opt => opt.MapFrom(src => src.Users.Count(u => u.Active)))
            .ForMember(dest => dest.TotalCustomers, opt => opt.MapFrom(src => src.Customers.Count))
            .ForMember(dest => dest.ActiveCustomers, opt => opt.MapFrom(src => src.Customers.Count(c => c.Active)))
            .ForMember(dest => dest.TotalNeighborhoods, opt => opt.MapFrom(src => src.Neighborhoods.Count))
            .ForMember(dest => dest.Users, opt => opt.MapFrom(src => src.Users))
            .ForMember(dest => dest.Neighborhoods, opt => opt.MapFrom(src => src.Neighborhoods))
            .ForMember(dest => dest.PrintSettings, opt => opt.MapFrom(src => src.PrintSettings));

        CreateMap<BranchPrintSettings, BranchPrintSettingsDto>()
            .ForMember(dest => dest.AgentTokenConfigured, opt => opt.MapFrom(src => !string.IsNullOrWhiteSpace(src.AgentTokenHash)))
            .ForMember(dest => dest.PaperWidthMm, opt => opt.MapFrom(src => src.PaperWidthMmKitchen));

        CreateMap<BranchPrintSettings, PrintAgentConfigDto>()
            .ForMember(d => d.ReceiptLogoUrl, o => o.Ignore())
            .ForMember(d => d.PaperWidthMm, o => o.MapFrom(s => s.PaperWidthMmKitchen));

        // Neighborhood -> BranchNeighborhoodDto
        CreateMap<Neighborhood, BranchNeighborhoodDto>()
            .ForMember(dest => dest.TotalCustomers, opt => opt.Ignore())
            .ForMember(dest => dest.TotalAddresses, opt => opt.Ignore());

        CreateMap<CreateNeighborhoodDto, CreateNeighborhoodCommand>()
            .ForMember(dest => dest.BranchId, opt => opt.Ignore());

        CreateMap<UpdateNeighborhoodDto, UpdateNeighborhoodCommand>()
            .ForMember(dest => dest.Id, opt => opt.Ignore());

        // User -> BranchUserDto
        CreateMap<User, BranchUserDto>()
            .ForMember(dest => dest.Role, opt => opt.MapFrom(src => src.Role.ToString()))
            .ForMember(dest => dest.Active, opt => opt.MapFrom(src => src.Active))
            .ForMember(dest => dest.PayrollExpenseName,
                opt => opt.MapFrom(src => src.PayrollExpense != null ? src.PayrollExpense.Name : null))
            .ForMember(dest => dest.LastLogin, opt => opt.Ignore());

        // Commands
        CreateMap<CreateBranchDto, CreateBranchCommand>();
        CreateMap<UpdateBranchDto, UpdateBranchCommand>()
            .ForMember(dest => dest.Id, opt => opt.Ignore()); // El Id lo sacamos de la ruta

        // Search / Query
        CreateMap<BranchSearchDto, GetBranchesQuery>();
    }
}
