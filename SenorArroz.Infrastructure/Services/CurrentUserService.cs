using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.Infrastructure.Services
{
    public class CurrentUserService : ICurrentUser
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public CurrentUserService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public string Id => _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value??string.Empty;

        public string Role => _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.Role)?.Value?.ToLower() ?? string.Empty;

        public int BranchId
        {
            get
            {
                var branchClaim = _httpContextAccessor.HttpContext?.User?.FindFirst("branch_id")?.Value;
                return branchClaim != null ? int.Parse(branchClaim) : 0;
            }
        }

        public bool IsAuthenticated => _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;

    }
}
