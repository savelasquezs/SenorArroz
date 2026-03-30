using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Infrastructure.Data.Configurations;

namespace SenorArroz.Infrastructure.Data
{
    public class ApplicationDbContext : DbContext, IApplicationDbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
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

        public virtual DbSet<Branch> Branches { get; set; }

        public virtual DbSet<BranchPrintSettings> BranchPrintSettings { get; set; }

        public virtual DbSet<PrintJob> PrintJobs { get; set; }

        public virtual DbSet<Customer> Customers { get; set; }

        public virtual DbSet<DeliverymanAdvance> DeliverymanAdvances { get; set; }

        public virtual DbSet<DeliverymanDayState> DeliverymanDayStates { get; set; }

        public virtual DbSet<DeliveryRoute> DeliveryRoutes { get; set; }

        public virtual DbSet<DeliveryRouteStop> DeliveryRouteStops { get; set; }

        public virtual DbSet<Expense> Expenses { get; set; }

        public virtual DbSet<ExpenseBankPayment> ExpenseBankPayments { get; set; }

        public virtual DbSet<ExpenseCategory> ExpenseCategories { get; set; }

        public virtual DbSet<ExpenseDetail> ExpenseDetails { get; set; }

        public virtual DbSet<ExpenseHeader> ExpenseHeaders { get; set; }

        public virtual DbSet<ExpenseMenuTarget> ExpenseMenuTargets { get; set; }

        public virtual DbSet<LoyaltyRule> LoyaltyRules { get; set; }

        public virtual DbSet<Neighborhood> Neighborhoods { get; set; }

        public virtual DbSet<Order> Orders { get; set; }

        public virtual DbSet<OrderDetail> OrderDetails { get; set; }

        public virtual DbSet<ReservationDeposit> ReservationDeposits { get; set; }

        public virtual DbSet<Product> Products { get; set; }

        public virtual DbSet<ProductCategory> ProductCategories { get; set; }

        public virtual DbSet<Supplier> Suppliers { get; set; }

        public virtual DbSet<SupplierExpense> SupplierExpenses { get; set; }

        public virtual DbSet<User> Users { get; set; }

        public virtual DbSet<RefreshToken> RefreshTokens { get; set; }
        public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new BranchConfiguration());
            modelBuilder.ApplyConfiguration(new BranchPrintSettingsConfiguration());
            modelBuilder.ApplyConfiguration(new PrintJobConfiguration());
            modelBuilder.ApplyConfiguration(new UserConfiguration());
            modelBuilder.ApplyConfiguration(new CustomerConfiguration());
            modelBuilder.ApplyConfiguration(new NeighborhoodConfiguration());
            modelBuilder.ApplyConfiguration(new AddressConfiguration());
            modelBuilder.ApplyConfiguration(new ProductCategoryConfiguration());
            modelBuilder.ApplyConfiguration(new ProductConfiguration());
            modelBuilder.ApplyConfiguration(new LoyaltyRuleConfiguration());
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
            modelBuilder.ApplyConfiguration(new DeliverymanAdvanceConfiguration());
            modelBuilder.ApplyConfiguration(new DeliverymanDayStateConfiguration());
            modelBuilder.ApplyConfiguration(new DeliveryRouteConfiguration());
            modelBuilder.ApplyConfiguration(new DeliveryRouteStopConfiguration());
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

            base.OnModelCreating(modelBuilder);
        }
        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            // Convert all DateTime properties to UTC before saving
            ConvertDateTimesToUtc();
            return await base.SaveChangesAsync(cancellationToken);
        }

        public override int SaveChanges()
        {
            // Convert all DateTime properties to UTC before saving
            ConvertDateTimesToUtc();
            return base.SaveChanges();
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