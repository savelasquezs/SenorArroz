namespace SenorArroz.Application.Features.Auth.DTOs
{
    public class UserInfoDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public int BranchId { get; set; }
        public string BranchName { get; set; } = string.Empty;
        public string BranchAddress { get; set; } = string.Empty;
        public string BranchPhone1 { get; set; } = string.Empty;
        public string? BranchPhone2 { get; set; }
        public decimal? BranchLatitude { get; set; }
        public decimal? BranchLongitude { get; set; }
        public string? ProfileImageUrl { get; set; }
    }
}