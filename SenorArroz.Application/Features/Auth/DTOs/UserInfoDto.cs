namespace SenorArroz.Application.Features.Auth.DTOs;

public class UserInfoDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public int BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
}