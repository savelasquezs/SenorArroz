using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Infrastructure.Data.Configurations;

namespace SenorArroz.Infrastructure.Data
{
    public class ApplicationDbContext : DbContext, IApplicationDbContext
    {
        private readonly ICurrentUser? _currentUser;

        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, ICurrentUser? currentUser = null)
            : base(options)
        {
            _currentUser = currentUser;
        }

        public virtual DbSet<Address> Addresses { get; set; }

        public virtual DbSet<App> Apps { get; set; }

        public virtual DbSet<AppPayment> AppPayments { get; set; }

        public virtual DbSet<Bank> Banks { get; set; }

        public virtual DbSet<BankPayment> BankPayments { get; set; }

        public virtual DbSet<BankTransfer> BankTransfers { get; set; }

        public virtual DbSet<CashRegisterClosure> CashRegisterClosures { get; set; }
        public virtual DbSet<CashClosureBankReconciliation> CashClosureBankReconciliations { get; set; }
        public virtual DbSet<CashClosureInformalLoan> CashClosureInformalLoans { get; set; }
        public virtual DbSet<CashVaultMovement> CashVaultMovements { get; set; }

        public virtual DbSet<BranchInformalLoan> BranchInformalLoans { get; set; }

        public virtual DbSet<BranchInformalLoanExemptOrder> BranchInformalLoanExemptOrders { get; set; }

        public virtual DbSet<Branch> Branches { get; set; }
        public virtual DbSet<BranchBusinessHour> BranchBusinessHours { get; set; }

        public virtual DbSet<BranchAiSetting> BranchAiSettings { get; set; }

        public virtual DbSet<BranchPrintSettings> BranchPrintSettings { get; set; }

        public virtual DbSet<WhatsAppBranchSetting> WhatsAppBranchSettings { get; set; }

        public virtual DbSet<WhatsAppConversation> WhatsAppConversations { get; set; }

        public virtual DbSet<WhatsAppMessage> WhatsAppMessages { get; set; }
        public virtual DbSet<WhatsAppAiInvocation> WhatsAppAiInvocations { get; set; }

        public virtual DbSet<WhatsAppQuickReply> WhatsAppQuickReplies { get; set; }

        public virtual DbSet<WhatsAppTemplate> WhatsAppTemplates { get; set; }

        public virtual DbSet<WhatsAppWebhookEvent> WhatsAppWebhookEvents { get; set; }
        public virtual DbSet<DeliveryAppConnection> DeliveryAppConnections { get; set; }
        public virtual DbSet<DeliveryAppProductMapping> DeliveryAppProductMappings { get; set; }
        public virtual DbSet<ExternalDeliveryOrder> ExternalDeliveryOrders { get; set; }
        public virtual DbSet<IntegrationWebhookEvent> IntegrationWebhookEvents { get; set; }

        public virtual DbSet<PrintJob> PrintJobs { get; set; }

        public virtual DbSet<Customer> Customers { get; set; }

        public virtual DbSet<DeliverymanAdvance> DeliverymanAdvances { get; set; }

        public virtual DbSet<DeliverymanDayState> DeliverymanDayStates { get; set; }

        public virtual DbSet<DeliverymanLocation> DeliverymanLocations { get; set; }

        public virtual DbSet<DeliveryDeviceEvent> DeliveryDeviceEvents { get; set; }

        public virtual DbSet<DeliveryStay> DeliveryStays { get; set; }

        public virtual DbSet<DeliveryWorkSession> DeliveryWorkSessions { get; set; }

        public virtual DbSet<DeliveryRoute> DeliveryRoutes { get; set; }

        public virtual DbSet<DeliveryRouteStop> DeliveryRouteStops { get; set; }

        public virtual DbSet<DailyAuditDispatch> DailyAuditDispatches { get; set; }
        public virtual DbSet<DailyPromotion> DailyPromotions { get; set; }
        public virtual DbSet<DailyPromotionProduct> DailyPromotionProducts { get; set; }
        public virtual DbSet<DiscountCode> DiscountCodes { get; set; }
        public virtual DbSet<EmailOutboxMessage> EmailOutboxMessages { get; set; }
        public virtual DbSet<EntityAuditLog> EntityAuditLogs { get; set; }

        public virtual DbSet<Expense> Expenses { get; set; }

        public virtual DbSet<ExpenseBankPayment> ExpenseBankPayments { get; set; }

        public virtual DbSet<ExpenseCategory> ExpenseCategories { get; set; }

        public virtual DbSet<ExpenseDetail> ExpenseDetails { get; set; }

        public virtual DbSet<ExpenseHeader> ExpenseHeaders { get; set; }

        public virtual DbSet<ExpenseMenuTarget> ExpenseMenuTargets { get; set; }

        public virtual DbSet<LoyaltyCycleStep> LoyaltyCycleSteps { get; set; }

        public virtual DbSet<Neighborhood> Neighborhoods { get; set; }

        public virtual DbSet<Order> Orders { get; set; }

        public virtual DbSet<OrderDetail> OrderDetails { get; set; }

        public virtual DbSet<ReservationDeposit> ReservationDeposits { get; set; }

        public virtual DbSet<Product> Products { get; set; }
        public virtual DbSet<CommercialProfile> CommercialProfiles { get; set; }

        public virtual DbSet<ProductCategory> ProductCategories { get; set; }

        public virtual DbSet<Supplier> Suppliers { get; set; }

        public virtual DbSet<SupplierExpense> SupplierExpenses { get; set; }

        public virtual DbSet<User> Users { get; set; }

        public virtual DbSet<UserDeviceToken> UserDeviceTokens { get; set; }

        public virtual DbSet<RefreshToken> RefreshTokens { get; set; }
        public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new BranchConfiguration());
            modelBuilder.ApplyConfiguration(new BranchBusinessHourConfiguration());
            modelBuilder.ApplyConfiguration(new BranchAiSettingConfiguration());
            modelBuilder.ApplyConfiguration(new BranchPrintSettingsConfiguration());
            modelBuilder.ApplyConfiguration(new WhatsAppBranchSettingConfiguration());
            modelBuilder.ApplyConfiguration(new WhatsAppConversationConfiguration());
            modelBuilder.ApplyConfiguration(new WhatsAppMessageConfiguration());
            modelBuilder.ApplyConfiguration(new WhatsAppAiInvocationConfiguration());
            modelBuilder.ApplyConfiguration(new WhatsAppQuickReplyConfiguration());
            modelBuilder.ApplyConfiguration(new WhatsAppTemplateConfiguration());
            modelBuilder.ApplyConfiguration(new WhatsAppWebhookEventConfiguration());
            modelBuilder.ApplyConfiguration(new DeliveryAppConnectionConfiguration());
            modelBuilder.ApplyConfiguration(new DeliveryAppProductMappingConfiguration());
            modelBuilder.ApplyConfiguration(new ExternalDeliveryOrderConfiguration());
            modelBuilder.ApplyConfiguration(new IntegrationWebhookEventConfiguration());
            modelBuilder.ApplyConfiguration(new PrintJobConfiguration());
            modelBuilder.ApplyConfiguration(new UserConfiguration());
            modelBuilder.ApplyConfiguration(new CustomerConfiguration());
            modelBuilder.ApplyConfiguration(new NeighborhoodConfiguration());
            modelBuilder.ApplyConfiguration(new AddressConfiguration());
            modelBuilder.ApplyConfiguration(new ProductCategoryConfiguration());
            modelBuilder.ApplyConfiguration(new ProductConfiguration());
            modelBuilder.ApplyConfiguration(new CommercialProfileConfiguration());
            modelBuilder.ApplyConfiguration(new LoyaltyCycleStepConfiguration());
            modelBuilder.ApplyConfiguration(new OrderConfiguration());
            modelBuilder.ApplyConfiguration(new OrderDetailConfiguration());
            modelBuilder.ApplyConfiguration(new BankConfiguration());
            modelBuilder.ApplyConfiguration(new AppConfiguration());
            modelBuilder.ApplyConfiguration(new AppPaymentConfiguration());
            modelBuilder.ApplyConfiguration(new BankPaymentConfiguration());
            modelBuilder.ApplyConfiguration(new BankTransferConfiguration());
            modelBuilder.ApplyConfiguration(new CashRegisterClosureConfiguration());
            modelBuilder.ApplyConfiguration(new CashClosureBankReconciliationConfiguration());
            modelBuilder.ApplyConfiguration(new CashClosureInformalLoanConfiguration());
            modelBuilder.ApplyConfiguration(new CashVaultMovementConfiguration());
            modelBuilder.ApplyConfiguration(new BranchInformalLoanConfiguration());
            modelBuilder.ApplyConfiguration(new BranchInformalLoanExemptOrderConfiguration());
            modelBuilder.ApplyConfiguration(new DeliverymanAdvanceConfiguration());
            modelBuilder.ApplyConfiguration(new DeliverymanDayStateConfiguration());
            modelBuilder.ApplyConfiguration(new DeliverymanLocationConfiguration());
            modelBuilder.ApplyConfiguration(new DeliveryDeviceEventConfiguration());
            modelBuilder.ApplyConfiguration(new DeliveryStayConfiguration());
            modelBuilder.ApplyConfiguration(new DeliveryWorkSessionConfiguration());
            modelBuilder.ApplyConfiguration(new DeliveryRouteConfiguration());
            modelBuilder.ApplyConfiguration(new DeliveryRouteStopConfiguration());
            modelBuilder.ApplyConfiguration(new DailyAuditDispatchConfiguration());
            modelBuilder.ApplyConfiguration(new DailyPromotionConfiguration());
            modelBuilder.ApplyConfiguration(new DailyPromotionProductConfiguration());
            modelBuilder.ApplyConfiguration(new DiscountCodeConfiguration());
            modelBuilder.ApplyConfiguration(new EmailOutboxMessageConfiguration());
            modelBuilder.ApplyConfiguration(new EntityAuditLogConfiguration());
            modelBuilder.ApplyConfiguration(new SupplierConfiguration());
            modelBuilder.ApplyConfiguration(new ExpenseCategoryConfiguration());
            modelBuilder.ApplyConfiguration(new ExpenseConfiguration());
            modelBuilder.ApplyConfiguration(new ExpenseHeaderConfiguration());
            modelBuilder.ApplyConfiguration(new ExpenseDetailConfiguration());
            modelBuilder.ApplyConfiguration(new ExpenseMenuTargetConfiguration());
            modelBuilder.ApplyConfiguration(new ExpenseBankPaymentConfiguration());
            modelBuilder.ApplyConfiguration(new SupplierExpenseConfiguration());
            modelBuilder.ApplyConfiguration(new ReservationDepositConfiguration());
            modelBuilder.ApplyConfiguration(new RefreshTokenConfiguration());
            modelBuilder.ApplyConfiguration(new PasswordResetTokenConfiguration());
            modelBuilder.ApplyConfiguration(new UserDeviceTokenConfiguration());

            base.OnModelCreating(modelBuilder);
        }
        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            ConvertDateTimesToUtc();
            await ApplyAuditSessionContextAsync(cancellationToken);
            return await base.SaveChangesAsync(cancellationToken);
        }

        public override int SaveChanges()
        {
            ConvertDateTimesToUtc();
            ApplyAuditSessionContext();
            return base.SaveChanges();
        }

        private async Task ApplyAuditSessionContextAsync(CancellationToken cancellationToken)
        {
            if (!Database.IsNpgsql())
                return;

            var userId = _currentUser?.IsAuthenticated == true ? _currentUser.Id.ToString() : string.Empty;
            var branchId = _currentUser?.BranchId > 0 ? _currentUser.BranchId.ToString() : string.Empty;

            await Database.ExecuteSqlRawAsync("select set_config('app.current_user_id', {0}, true);", [userId], cancellationToken);
            await Database.ExecuteSqlRawAsync("select set_config('app.current_user_name', {0}, true);", [string.Empty], cancellationToken);
            await Database.ExecuteSqlRawAsync("select set_config('app.current_branch_id', {0}, true);", [branchId], cancellationToken);
        }

        private void ApplyAuditSessionContext()
        {
            if (!Database.IsNpgsql())
                return;

            var userId = _currentUser?.IsAuthenticated == true ? _currentUser.Id.ToString() : string.Empty;
            var branchId = _currentUser?.BranchId > 0 ? _currentUser.BranchId.ToString() : string.Empty;

            Database.ExecuteSqlRaw("select set_config('app.current_user_id', {0}, true);", userId);
            Database.ExecuteSqlRaw("select set_config('app.current_user_name', {0}, true);", string.Empty);
            Database.ExecuteSqlRaw("select set_config('app.current_branch_id', {0}, true);", branchId);
        }

        private void ConvertDateTimesToUtc()
        {
            var entries = ChangeTracker.Entries()
                .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified);

            foreach (var entry in entries)
            {
                foreach (var property in entry.Properties)
                {
                    // Skip CreatedAt and UpdatedAt as they are handled by database triggers
                    if (property.Metadata.Name == "CreatedAt" || property.Metadata.Name == "UpdatedAt")
                        continue;

                    if (property.CurrentValue is DateTime dateTime && dateTime.Kind != DateTimeKind.Utc)
                    {
                        property.CurrentValue = DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
                    }
                }
            }
        }
    }
}
