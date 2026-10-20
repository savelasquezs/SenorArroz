using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.Users.DTOs
{
    public class CreateUserDto
    {
        public int BranchId { get; set; }
        public UserRole Role { get; set; }
        public EmployeeAccountType AccountType { get; set; } = EmployeeAccountType.Personal;
        public string? ConfirmationPassword { get; set; }
        public bool DeliveryAppEnabled { get; set; }
        public bool? WebAccessEnabled { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public int? PayrollExpenseId { get; set; }
    }
}
