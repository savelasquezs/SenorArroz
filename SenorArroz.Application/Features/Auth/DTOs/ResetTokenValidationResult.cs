namespace SenorArroz.Application.Features.Auth.DTOs
{
    public class ResetTokenValidationResult
    {
        public bool IsValid { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? UserName { get; set; }
        public DateTime? ExpiresAt { get; set; }
    }
}