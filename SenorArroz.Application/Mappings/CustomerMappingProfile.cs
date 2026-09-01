using AutoMapper;
using SenorArroz.Application.Features.Customers.Commands;
using SenorArroz.Application.Features.Customers.DTOs;
using SenorArroz.Application.Features.Customers.Queries;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Mappings;

public class CustomerMappingProfile : Profile
{
    public CustomerMappingProfile()
    {
        // Customer mappings
        CreateMap<Customer, CustomerDto>()
            .ForMember(dest => dest.BranchName, opt => opt.MapFrom(src => src.Branch.Name))
            .ForMember(dest => dest.Addresses, opt => opt.MapFrom(src => src.Addresses))
            .ForMember(dest => dest.TotalOrders, opt => opt.Ignore()) // Will be set manually
            .ForMember(dest => dest.FirstOrderDate, opt => opt.Ignore())
            .ForMember(dest => dest.LastOrderDate, opt => opt.Ignore()) // Will be set manually
            .ForMember(dest => dest.TotalAccumulated, opt => opt.Ignore())
            .ForMember(dest => dest.HasWhatsAppIdentity, opt => opt.MapFrom(src => !string.IsNullOrWhiteSpace(src.WhatsAppUserId)))
            .ForMember(dest => dest.LoyaltyDeliveredCount, opt => opt.Ignore())
            .ForMember(dest => dest.LoyaltyNextStepIndex, opt => opt.Ignore())
            .ForMember(dest => dest.LoyaltyNextRewardLabel, opt => opt.Ignore())
            .ForMember(dest => dest.LoyaltyDeliveriesUntilNextReward, opt => opt.Ignore())
            .ForMember(dest => dest.LoyaltyRewardDueOnCurrentOrder, opt => opt.Ignore())
            .ForMember(dest => dest.LoyaltyNextRewardMessage, opt => opt.Ignore());

        CreateMap<CreateCustomerDto, CreateCustomerCommand>();
        CreateMap<UpdateCustomerDto, UpdateCustomerCommand>()
            .ForMember(dest => dest.Id, opt => opt.Ignore()); // Will be set from route

        // Address mappings
        CreateMap<Address, CustomerAddressDto>()
            .ForMember(dest => dest.NeighborhoodName, opt => opt.MapFrom(src => src.Neighborhood != null ? src.Neighborhood.Name : null))
            .ForMember(dest => dest.Address, opt => opt.MapFrom(src => src.AddressText))
            .ForMember(dest => dest.IsPrimary, opt => opt.MapFrom(src => src.IsPrimary));

        CreateMap<CreateCustomerAddressDto, CreateAddressCommand>()
            .ForMember(dest => dest.CustomerId, opt => opt.Ignore()); // Will be set from route

        CreateMap<UpdateCustomerAddressDto, UpdateAddressCommand>()
            .ForMember(dest => dest.Id, opt => opt.Ignore()); // Will be set from route

        // Search mappings
        CreateMap<CustomerSearchDto, GetCustomersQuery>();
    }
}
