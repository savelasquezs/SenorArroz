using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Common.Interfaces
{
    public interface IApplicationDbContext
    {
        DbSet<Address> Addresses { get; set; }

        DbSet<App> Apps { get; set; }

        DbSet<AppPayment> AppPayments { get; set; }

        DbSet<Bank> Banks { get; set; }

        DbSet<BankPayment> BankPayments { get; set; }

        DbSet<BankTransfer> BankTransfers { get; set; }

        DbSet<CashRegisterClosure> CashRegisterClosures { get; set; }
        DbSet<CashClosureBankReconciliation> CashClosureBankReconciliations { get; set; }
        DbSet<CashClosureInformalLoan> CashClosureInformalLoans { get; set; }

        DbSet<Branch> Branches { get; set; }

        DbSet<Customer> Customers { get; set; }

        DbSet<DeliverymanAdvance> DeliverymanAdvances { get; set; }

        DbSet<Expense> Expenses { get; set; }

        DbSet<ExpenseBankPayment> ExpenseBankPayments { get; set; }

        DbSet<ExpenseCategory> ExpenseCategories { get; set; }

        DbSet<ExpenseDetail> ExpenseDetails { get; set; }

        DbSet<ExpenseHeader> ExpenseHeaders { get; set; }

        DbSet<LoyaltyRule> LoyaltyRules { get; set; }

        DbSet<Neighborhood> Neighborhoods { get; set; }

        DbSet<Order> Orders { get; set; }

        DbSet<ReservationDeposit> ReservationDeposits { get; set; }

        DbSet<OrderDetail> OrderDetails { get; set; }

        DbSet<Product> Products { get; set; }

        DbSet<ProductCategory> ProductCategories { get; set; }

        DbSet<Supplier> Suppliers { get; set; }

        DbSet<SupplierExpense> SupplierExpenses { get; set; }

        DbSet<User> Users { get; set; }

        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}