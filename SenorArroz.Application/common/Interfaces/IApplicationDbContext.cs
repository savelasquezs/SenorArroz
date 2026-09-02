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
        DbSet<BranchBusinessHour> BranchBusinessHours { get; set; }

        DbSet<BranchAiSetting> BranchAiSettings { get; set; }

        DbSet<BranchPrintSettings> BranchPrintSettings { get; set; }

        DbSet<BusinessDocument> BusinessDocuments { get; set; }

        DbSet<WhatsAppBranchSetting> WhatsAppBranchSettings { get; set; }

        DbSet<WhatsAppConversation> WhatsAppConversations { get; set; }

        DbSet<WhatsAppMessage> WhatsAppMessages { get; set; }
        DbSet<WhatsAppAiInvocation> WhatsAppAiInvocations { get; set; }

        DbSet<WhatsAppQuickReply> WhatsAppQuickReplies { get; set; }

        DbSet<WhatsAppTemplate> WhatsAppTemplates { get; set; }

        DbSet<WhatsAppWebhookEvent> WhatsAppWebhookEvents { get; set; }
        DbSet<DeliveryAppConnection> DeliveryAppConnections { get; set; }
        DbSet<DeliveryAppStore> DeliveryAppStores { get; set; }
        DbSet<DeliveryAppWebhookSubscription> DeliveryAppWebhookSubscriptions { get; set; }
        DbSet<DeliveryAppProductMapping> DeliveryAppProductMappings { get; set; }
        DbSet<ExternalDeliveryOrder> ExternalDeliveryOrders { get; set; }
        DbSet<IntegrationWebhookEvent> IntegrationWebhookEvents { get; set; }
        DbSet<RappiMenuPublication> RappiMenuPublications { get; set; }
        DbSet<RappiAvailabilityState> RappiAvailabilityStates { get; set; }
        DbSet<WompiPaymentIntegration> WompiPaymentIntegrations { get; set; }
        DbSet<WompiPaymentAttempt> WompiPaymentAttempts { get; set; }
        DbSet<StorefrontCheckout> StorefrontCheckouts { get; set; }
        DbSet<WompiProviderTransaction> WompiProviderTransactions { get; set; }
        DbSet<WompiWebhookEvent> WompiWebhookEvents { get; set; }
        DbSet<PaymentNotificationOutboxMessage> PaymentNotificationOutboxMessages { get; set; }

        DbSet<PrintJob> PrintJobs { get; set; }

        DbSet<Customer> Customers { get; set; }

        DbSet<StorefrontCustomerAuthChallenge> StorefrontCustomerAuthChallenges { get; set; }

        DbSet<DeliverymanAdvance> DeliverymanAdvances { get; set; }

        DbSet<DeliverymanDayState> DeliverymanDayStates { get; set; }

        DbSet<DeliverymanLocation> DeliverymanLocations { get; set; }

        DbSet<DeliveryDeviceEvent> DeliveryDeviceEvents { get; set; }

        DbSet<DeliveryAuthorizedPlace> DeliveryAuthorizedPlaces { get; set; }

        DbSet<DeliveryStay> DeliveryStays { get; set; }

        DbSet<DeliveryTrackingIncident> DeliveryTrackingIncidents { get; set; }

        DbSet<DeliveryTrackingAlert> DeliveryTrackingAlerts { get; set; }

        DbSet<DeliveryIncidentLocationEvidence> DeliveryIncidentLocationEvidence { get; set; }

        DbSet<DeliveryIncidentDeviceEventEvidence> DeliveryIncidentDeviceEventEvidence { get; set; }

        DbSet<DeliveryWorkSession> DeliveryWorkSessions { get; set; }

        DbSet<DeliveryRoute> DeliveryRoutes { get; set; }

        DbSet<DeliveryRouteStop> DeliveryRouteStops { get; set; }

        DbSet<DeliveryRoutingPlan> DeliveryRoutingPlans { get; set; }

        DbSet<DeliveryRouteProposal> DeliveryRouteProposals { get; set; }

        DbSet<DeliveryRouteProposalStop> DeliveryRouteProposalStops { get; set; }

        DbSet<DailyAuditDispatch> DailyAuditDispatches { get; set; }
        DbSet<DailyPromotion> DailyPromotions { get; set; }
        DbSet<DailyPromotionProduct> DailyPromotionProducts { get; set; }
        DbSet<DiscountCode> DiscountCodes { get; set; }
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
        DbSet<CommercialProfile> CommercialProfiles { get; set; }

        DbSet<ProductCategory> ProductCategories { get; set; }

        DbSet<Supplier> Suppliers { get; set; }

        DbSet<SupplierExpense> SupplierExpenses { get; set; }

        DbSet<User> Users { get; set; }

        DbSet<UserDeviceToken> UserDeviceTokens { get; set; }

        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
