using System.Linq.Expressions;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Domain.Helpers;

public static class UserSessionRules
{
    public static void Invalidate(User user)
    {
        user.ActiveSessionId = null;
        user.ActiveDeliverySessionId = null;
    }

    public static bool HasSeparateDeliverySession(User user) =>
        user.Role is UserRole.Admin or UserRole.Superadmin;

    public static Guid? GetSessionId(User user, bool deliveryApp) =>
        deliveryApp && HasSeparateDeliverySession(user) ? user.ActiveDeliverySessionId : user.ActiveSessionId;

    public static bool IsCurrent(User user, Guid? sessionId, string effectiveRole) =>
        sessionId.HasValue && GetSessionId(user,
            string.Equals(effectiveRole, "Deliveryman", StringComparison.OrdinalIgnoreCase)) == sessionId;

    public static Expression<Func<User, bool>> CurrentSession(Guid? sessionId, bool deliveryApp) => user =>
        sessionId.HasValue && user.Active &&
        (deliveryApp && (user.Role == UserRole.Admin || user.Role == UserRole.Superadmin)
            ? user.ActiveDeliverySessionId == sessionId
            : user.ActiveSessionId == sessionId);
}
