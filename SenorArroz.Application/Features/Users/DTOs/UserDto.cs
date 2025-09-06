using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.Users.DTOs
{
    public class UserDto
    {
        public int Id { get; set; }
        public int BranchId { get; set; }
        public string BranchName { get; set; } = string.Empty;
        public UserRole Role { get; set; }
        public string RoleName => Role.ToString().ToLower();
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public bool Active { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}