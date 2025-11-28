// SenorArroz.Application/Mappings/ExpenseMappingProfile.cs
using AutoMapper;
using SenorArroz.Application.Features.Expenses.Commands;
using SenorArroz.Application.Features.Expenses.DTOs;
using SenorArroz.Application.Features.Expenses.Queries;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Mappings;

public class ExpenseMappingProfile : Profile
{
    public ExpenseMappingProfile()
    {
        // Expense mappings
        CreateMap<Expense, ExpenseDto>()
            .ForMember(dest => dest.CategoryName, opt => opt.MapFrom(src => src.Category.Name))
            .ForMember(dest => dest.UnitDisplay, opt => opt.MapFrom(src => GetUnitDisplay(src.Unit)));

        CreateMap<CreateExpenseDto, CreateExpenseCommand>();
        CreateMap<UpdateExpenseDto, UpdateExpenseCommand>()
            .ForMember(dest => dest.Id, opt => opt.Ignore());

        CreateMap<ExpenseSearchDto, GetExpensesQuery>();
    }

    private static string GetUnitDisplay(ExpenseUnit unit)
    {
        return unit switch
        {
            ExpenseUnit.Unit => "Unidad",
            ExpenseUnit.Kilo => "Kilo",
            ExpenseUnit.Package => "Paquete",
            ExpenseUnit.Pound => "Libra",
            ExpenseUnit.Gallon => "Galón",
            _ => unit.ToString()
        };
    }
}

