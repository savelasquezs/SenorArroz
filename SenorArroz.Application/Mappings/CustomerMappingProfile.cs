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
            .ForMember(dest => dest.LastOrderDate, opt => opt.Ignore()); // Will be set manually

        CreateMap<CreateCustomerDto, CreateCustomerCommand>();
        CreateMap<UpdateCustomerDto, UpdateCustomerCommand>()
            .ForMember(dest => dest.Id, opt => opt.Ignore()); // Will be set from route

        // Address mappings
        CreateMap<Address, CustomerAddressDto>()
            .ForMember(dest => dest.NeighborhoodName, opt => opt.MapFrom(src => src.Neighborhood.Name))
            .ForMember(dest => dest.Address, opt => opt.MapFrom(src => src.AddressText))
            .ForMember(dest => dest.IsPrimary, opt => opt.Ignore()); // Placeholder for future implementation

        CreateMap<CreateCustomerAddressDto, CreateAddressCommand>()
            .ForMember(dest => dest.CustomerId, opt => opt.Ignore()); // Will be set from route

        CreateMap<UpdateCustomerAddressDto, UpdateAddressCommand>()
            .ForMember(dest => dest.Id, opt => opt.Ignore()); // Will be set from route

        // Search mappings
        CreateMap<CustomerSearchDto, GetCustomersQuery>();
    }
}