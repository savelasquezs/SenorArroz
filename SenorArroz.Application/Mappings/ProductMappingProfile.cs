// SenorArroz.Application/Mappings/ProductMappingProfile.cs
using AutoMapper;
using SenorArroz.Application.Features.Products.Commands;
using SenorArroz.Application.Features.Products.DTOs;
using SenorArroz.Application.Features.Products.Queries;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Mappings;

public class ProductMappingProfile : Profile
{
    public ProductMappingProfile()
    {
        // Product Category mappings
        CreateMap<ProductCategory, ProductCategoryDto>()
            .ForMember(dest => dest.BranchName, opt => opt.MapFrom(src => src.Branch.Name))
            .ForMember(dest => dest.TotalProducts, opt => opt.Ignore())
            .ForMember(dest => dest.ActiveProducts, opt => opt.Ignore());

        CreateMap<CreateProductCategoryDto, CreateProductCategoryCommand>();
        CreateMap<UpdateProductCategoryDto, UpdateProductCategoryCommand>()
            .ForMember(dest => dest.Id, opt => opt.Ignore());

        CreateMap<ProductCategorySearchDto, GetProductCategoriesQuery>();

        // Product mappings
        CreateMap<Product, ProductDto>()
            .ForMember(dest => dest.CategoryName, opt => opt.MapFrom(src => src.Category.Name))
            .ForMember(dest => dest.BranchId, opt => opt.MapFrom(src => src.Category.BranchId))
            .ForMember(dest => dest.BranchName, opt => opt.MapFrom(src => src.Category.Branch.Name))
            .ForMember(dest => dest.CommercialProfileName, opt => opt.MapFrom(src => src.CommercialProfile == null ? null : src.CommercialProfile.Name))
            .ForMember(dest => dest.Description, opt => opt.MapFrom(src => src.CommercialProfile == null ? null : src.CommercialProfile.Description))
            .ForMember(dest => dest.Ingredients, opt => opt.MapFrom(src => src.CommercialProfile == null ? null : src.CommercialProfile.Ingredients))
            .ForMember(dest => dest.PhotoUrl, opt => opt.MapFrom(src => src.CommercialProfile == null ? null : src.CommercialProfile.PhotoUrl));
            

        CreateMap<Product, ProductDetailDto>()
            .ForMember(dest => dest.CategoryName, opt => opt.MapFrom(src => src.Category.Name))
            .ForMember(dest => dest.BranchId, opt => opt.MapFrom(src => src.Category.BranchId))
            .ForMember(dest => dest.BranchName, opt => opt.MapFrom(src => src.Category.Branch.Name))
            .ForMember(dest => dest.CommercialProfileName, opt => opt.MapFrom(src => src.CommercialProfile == null ? null : src.CommercialProfile.Name))
            .ForMember(dest => dest.Description, opt => opt.MapFrom(src => src.CommercialProfile == null ? null : src.CommercialProfile.Description))
            .ForMember(dest => dest.Ingredients, opt => opt.MapFrom(src => src.CommercialProfile == null ? null : src.CommercialProfile.Ingredients))
            .ForMember(dest => dest.PhotoUrl, opt => opt.MapFrom(src => src.CommercialProfile == null ? null : src.CommercialProfile.PhotoUrl))
            .ForMember(dest => dest.TotalSales, opt => opt.Ignore())
            .ForMember(dest => dest.TotalRevenue, opt => opt.Ignore())
            .ForMember(dest => dest.TotalOrders, opt => opt.Ignore())
            .ForMember(dest => dest.TotalCustomers, opt => opt.Ignore())
            .ForMember(dest => dest.LastSoldAt, opt => opt.Ignore())
            .ForMember(dest => dest.SalesUnitsEvolution, opt => opt.Ignore());

        CreateMap<CreateProductDto, CreateProductCommand>();
        CreateMap<UpdateProductDto, UpdateProductCommand>()
            .ForMember(dest => dest.Id, opt => opt.Ignore());

        CreateMap<ProductSearchDto, GetProductsQuery>();
    }
}
