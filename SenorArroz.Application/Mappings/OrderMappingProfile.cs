using AutoMapper;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Application.Features.BankPayments.DTOs;
using SenorArroz.Application.Features.AppPayments.DTOs;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Mappings;

public class OrderMappingProfile : Profile
{
    public OrderMappingProfile()
    {
        // Order -> OrderDto
        CreateMap<Order, OrderDto>()
            .ForMember(dest => dest.BranchName, opt => opt.MapFrom(src => src.Branch.Name))
            .ForMember(dest => dest.TakenByName, opt => opt.MapFrom(src => src.TakenBy.Name))
            .ForMember(dest => dest.CustomerName, opt => opt.MapFrom(src => src.Customer != null ? src.Customer.Name : null))
            .ForMember(dest => dest.CustomerPhone, opt => opt.MapFrom(src => src.Customer != null ? src.Customer.Phone1 : null))
            .ForMember(dest => dest.AddressDescription, opt => opt.MapFrom(src => src.Address != null ? src.Address.AddressText : null))
            .ForMember(dest => dest.NeighborhoodId, opt => opt.MapFrom(src => src.Address != null && src.Address.Neighborhood != null ? src.Address.Neighborhood.Id : (int?)null))
            .ForMember(dest => dest.NeighborhoodName, opt => opt.MapFrom(src => src.Address != null && src.Address.Neighborhood != null ? src.Address.Neighborhood.Name : null))
            .ForMember(dest => dest.Latitude, opt => opt.MapFrom(src => src.Address != null ? src.Address.Latitude : null))
            .ForMember(dest => dest.Longitude, opt => opt.MapFrom(src => src.Address != null ? src.Address.Longitude : null))
            .ForMember(dest => dest.LoyaltyRuleName, opt => opt.MapFrom(src => src.LoyaltyRule != null ? src.LoyaltyRule.Description : null))
            .ForMember(dest => dest.DeliveryManName, opt => opt.MapFrom(src => src.DeliveryMan != null ? src.DeliveryMan.Name : null))
            .ForMember(dest => dest.TypeDisplayName, opt => opt.MapFrom(src => GetTypeDisplayName(src.Type)))
            .ForMember(dest => dest.StatusDisplayName, opt => opt.MapFrom(src => GetStatusDisplayName(src.Status)))
            .ForMember(dest => dest.StatusTimes, opt => opt.MapFrom(src => src.GetStatusTimes()))
            .ForMember(dest => dest.BankPayments, opt => opt.MapFrom(src => src.BankPayments))
            .ForMember(dest => dest.AppPayments, opt => opt.MapFrom(src => src.AppPayments));

        // Order -> OrderWithDetailsDto
        CreateMap<Order, OrderWithDetailsDto>()
            .IncludeBase<Order, OrderDto>()
            .ForMember(dest => dest.OrderDetails, opt => opt.MapFrom(src => src.OrderDetails))
            .ForMember(dest => dest.BankPayments, opt => opt.MapFrom(src => src.BankPayments))
            .ForMember(dest => dest.AppPayments, opt => opt.MapFrom(src => src.AppPayments));

        // OrderDetail -> OrderDetailDto
        CreateMap<OrderDetail, OrderDetailDto>()
            .ForMember(dest => dest.ProductName, opt => opt.MapFrom(src => src.Product.Name))
            .ForMember(dest => dest.ProductDescription, opt => opt.MapFrom(src => src.Product.Name)); // Using Name as description since Product doesn't have Description

        // CreateOrderDto -> Order
        CreateMap<CreateOrderDto, Order>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.Status, opt => opt.Ignore())
            .ForMember(dest => dest.StatusTimes, opt => opt.Ignore())
            .ForMember(dest => dest.CancelledReason, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.Branch, opt => opt.Ignore())
            .ForMember(dest => dest.TakenBy, opt => opt.Ignore())
            .ForMember(dest => dest.Customer, opt => opt.Ignore())
            .ForMember(dest => dest.Address, opt => opt.Ignore())
            .ForMember(dest => dest.LoyaltyRule, opt => opt.Ignore())
            .ForMember(dest => dest.DeliveryMan, opt => opt.Ignore())
            .ForMember(dest => dest.OrderDetails, opt => opt.Ignore())
            .ForMember(dest => dest.BankPayments, opt => opt.Ignore())
            .ForMember(dest => dest.AppPayments, opt => opt.Ignore());

        // CreateOrderDetailDto -> OrderDetail
        CreateMap<CreateOrderDetailDto, OrderDetail>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.OrderId, opt => opt.Ignore())
            .ForMember(dest => dest.Subtotal, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.Order, opt => opt.Ignore())
            .ForMember(dest => dest.Product, opt => opt.Ignore());

        // UpdateOrderDto -> Order (para actualizaciones)
        CreateMap<UpdateOrderDto, Order>()
            .ForAllMembers(opt => opt.Condition((src, dest, srcMember) => srcMember != null));

        // UpdateOrderDetailDto -> OrderDetail (para actualizaciones)
        CreateMap<UpdateOrderDetailDto, OrderDetail>()
            .ForAllMembers(opt => opt.Condition((src, dest, srcMember) => srcMember != null));

        // BankPayment -> BankPaymentDto
        CreateMap<BankPayment, BankPaymentDto>()
            .ForMember(dest => dest.BankName, opt => opt.MapFrom(src => src.Bank.Name));

        // AppPayment -> AppPaymentDto
        CreateMap<AppPayment, AppPaymentDto>()
            .ForMember(dest => dest.AppName, opt => opt.MapFrom(src => src.App.Name))
            .ForMember(dest => dest.BankName, opt => opt.MapFrom(src => src.App.Bank.Name));
    }

    private static string GetTypeDisplayName(Domain.Enums.OrderType? type)
    {
        return type switch
        {
            Domain.Enums.OrderType.Onsite => "En el local",
            Domain.Enums.OrderType.Delivery => "Domicilio",
            Domain.Enums.OrderType.Reservation => "Reserva",
            _ => "No especificado"
        };
    }

    private static string GetStatusDisplayName(Domain.Enums.OrderStatus status)
    {
        return status switch
        {
            Domain.Enums.OrderStatus.Taken => "Tomado",
            Domain.Enums.OrderStatus.InPreparation => "En preparación",
            Domain.Enums.OrderStatus.Ready => "Listo",
            Domain.Enums.OrderStatus.OnTheWay => "En camino",
            Domain.Enums.OrderStatus.Delivered => "Entregado",
            Domain.Enums.OrderStatus.Cancelled => "Cancelado",
            _ => "Desconocido"
        };
    }
}
