using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.Users.DTOs
{
    public class UpdateUserDto
    {
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public UserRole Role { get; set; }
        public bool Active { get; set; }

        /// <summary>Nueva sucursal (solo aplica si el actor es superadmin).</summary>
        public int? BranchId { get; set; }

        public int? PayrollExpenseId { get; set; }
    }

    public sealed class SetDeliverymanWebAccessDto
    {
        public bool Enabled { get; set; }
    }
}
