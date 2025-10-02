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

        // Product mappings (coming next)
        CreateMap<Product, ProductDto>()
            .ForMember(dest => dest.CategoryName, opt => opt.MapFrom(src => src.Category.Name))
            .ForMember(dest => dest.BranchId, opt => opt.MapFrom(src => src.Category.BranchId))
            .ForMember(dest => dest.BranchName, opt => opt.MapFrom(src => src.Category.Branch.Name));

        CreateMap<CreateProductDto, CreateProductCommand>();
        CreateMap<UpdateProductDto, UpdateProductCommand>()
            .ForMember(dest => dest.Id, opt => opt.Ignore());

        CreateMap<ProductSearchDto, GetProductsQuery>();
    }
}