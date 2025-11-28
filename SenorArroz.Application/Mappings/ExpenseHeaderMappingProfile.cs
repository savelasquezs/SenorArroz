using AutoMapper;
using SenorArroz.Application.Features.ExpenseHeaders.DTOs;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Mappings;

public class ExpenseHeaderMappingProfile : Profile
{
    public ExpenseHeaderMappingProfile()
    {
        // ExpenseHeader -> ExpenseHeaderDto
        CreateMap<ExpenseHeader, ExpenseHeaderDto>()
            .ForMember(dest => dest.BranchName, opt => opt.MapFrom(src => src.Branch.Name))
            .ForMember(dest => dest.SupplierName, opt => opt.MapFrom(src => src.Supplier.Name))
            .ForMember(dest => dest.SupplierPhone, opt => opt.MapFrom(src => src.Supplier.Phone))
            .ForMember(dest => dest.CreatedByName, opt => opt.MapFrom(src => src.CreatedBy.Name))
            .ForMember(dest => dest.ExpenseDetails, opt => opt.MapFrom(src => src.ExpenseDetails))
            .ForMember(dest => dest.ExpenseBankPayments, opt => opt.MapFrom(src => src.ExpenseBankPayments))
            .ForMember(dest => dest.CategoryNames, opt => opt.Ignore()) // Se calcula en el handler
            .ForMember(dest => dest.BankNames, opt => opt.Ignore()) // Se calcula en el handler
            .ForMember(dest => dest.ExpenseNames, opt => opt.Ignore()); // Se calcula en el handler

        // ExpenseDetail -> ExpenseDetailDto
        CreateMap<ExpenseDetail, ExpenseDetailDto>()
            .ForMember(dest => dest.ExpenseName, opt => opt.MapFrom(src => src.Expense.Name))
            .ForMember(dest => dest.ExpenseCategoryName, opt => opt.MapFrom(src => src.Expense.Category.Name))
            .ForMember(dest => dest.ExpenseUnit, opt => opt.MapFrom(src => src.Expense.Unit.ToString()));

        // ExpenseBankPayment -> ExpenseBankPaymentDto
        CreateMap<ExpenseBankPayment, ExpenseBankPaymentDto>()
            .ForMember(dest => dest.BankName, opt => opt.MapFrom(src => src.Bank.Name));
    }
}


