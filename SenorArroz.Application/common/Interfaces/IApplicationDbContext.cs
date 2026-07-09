using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Common.Interfaces
{
    public interface IApplicationDbContext
    {
        DatabaseFacade Database { get; }
        DbSet<Address> Addresses { get; set; }

        DbSet<App> Apps { get; set; }

        DbSet<AppPayment> AppPayments { get; set; }

        DbSet<Bank> Banks { get; set; }

        DbSet<BankPayment> BankPayments { get; set; }

        DbSet<BankTransfer> BankTransfers { get; set; }

        DbSet<CashRegisterClosure> CashRegisterClosures { get; set; }
        DbSet<CashClosureBankReconciliation> CashClosureBankReconciliations { get; set; }
        DbSet<CashClosureInformalLoan> CashClosureInformalLoans { get; set; }
        DbSet<CashVaultMovement> CashVaultMovements { get; set; }

        DbSet<BranchInformalLoan> BranchInformalLoans { get; set; }

        DbSet<BranchInformalLoanExemptOrder> BranchInformalLoanExemptOrders { get; set; }

        DbSet<Branch> Branches { get; set; }

        DbSet<BranchAiSetting> BranchAiSettings { get; set; }

        DbSet<BranchPrintSettings> BranchPrintSettings { get; set; }

        DbSet<WhatsAppBranchSetting> WhatsAppBranchSettings { get; set; }

        DbSet<WhatsAppConversation> WhatsAppConversations { get; set; }

        DbSet<WhatsAppMessage> WhatsAppMessages { get; set; }

        DbSet<WhatsAppQuickReply> WhatsAppQuickReplies { get; set; }

        DbSet<WhatsAppTemplate> WhatsAppTemplates { get; set; }

        DbSet<WhatsAppWebhookEvent> WhatsAppWebhookEvents { get; set; }

        DbSet<PrintJob> PrintJobs { get; set; }

        DbSet<Customer> Customers { get; set; }

        DbSet<DeliverymanAdvance> DeliverymanAdvances { get; set; }

        DbSet<DeliverymanDayState> DeliverymanDayStates { get; set; }

        DbSet<DeliverymanLocation> DeliverymanLocations { get; set; }

        DbSet<DeliveryRoute> DeliveryRoutes { get; set; }

        DbSet<DeliveryRouteStop> DeliveryRouteStops { get; set; }

        DbSet<DailyAuditDispatch> DailyAuditDispatches { get; set; }
        DbSet<DailyPromotion> DailyPromotions { get; set; }
        DbSet<DailyPromotionProduct> DailyPromotionProducts { get; set; }
        DbSet<EmailOutboxMessage> EmailOutboxMessages { get; set; }
        DbSet<EntityAuditLog> EntityAuditLogs { get; set; }

        DbSet<Expense> Expenses { get; set; }

        DbSet<ExpenseBankPayment> ExpenseBankPayments { get; set; }

        DbSet<ExpenseCategory> ExpenseCategories { get; set; }

        DbSet<ExpenseDetail> ExpenseDetails { get; set; }

        DbSet<ExpenseHeader> ExpenseHeaders { get; set; }

        DbSet<ExpenseMenuTarget> ExpenseMenuTargets { get; set; }

        DbSet<LoyaltyCycleStep> LoyaltyCycleSteps { get; set; }

        DbSet<Neighborhood> Neighborhoods { get; set; }

        DbSet<Order> Orders { get; set; }

        DbSet<ReservationDeposit> ReservationDeposits { get; set; }

        DbSet<OrderDetail> OrderDetails { get; set; }

        DbSet<Product> Products { get; set; }

        DbSet<ProductCategory> ProductCategories { get; set; }

        DbSet<Supplier> Suppliers { get; set; }

        DbSet<SupplierExpense> SupplierExpenses { get; set; }

        DbSet<User> Users { get; set; }

        DbSet<UserDeviceToken> UserDeviceTokens { get; set; }

        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
