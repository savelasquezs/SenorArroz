// SenorArroz.Application/Mappings/ExpenseCategoryMappingProfile.cs
using AutoMapper;
using SenorArroz.Application.Features.ExpenseCategories.Commands;
using SenorArroz.Application.Features.ExpenseCategories.DTOs;
using SenorArroz.Application.Features.ExpenseCategories.Queries;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Mappings;

public class ExpenseCategoryMappingProfile : Profile
{
    public ExpenseCategoryMappingProfile()
    {
        // Expense Category mappings
        CreateMap<ExpenseCategory, ExpenseCategoryDto>()
            .ForMember(dest => dest.TotalExpenses, opt => opt.Ignore());

        CreateMap<CreateExpenseCategoryDto, CreateExpenseCategoryCommand>();
        CreateMap<UpdateExpenseCategoryDto, UpdateExpenseCategoryCommand>()
            .ForMember(dest => dest.Id, opt => opt.Ignore());

        CreateMap<ExpenseCategorySearchDto, GetExpenseCategoriesQuery>();
    }
}


