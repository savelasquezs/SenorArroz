using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.API.Filters;

public static class EmployeeAccountAccess
{
    public static bool Allows(User user, string controller, string action, int? targetUserId)
    {
        if (!user.Active) return false;
        if (user.Role != UserRole.Kitchen) return !user.IsTerminal;
        if (!user.WebAccessEnabled) return false;
        if (controller == "Auth") return true; // Sus handlers conservan la autenticación y el alcance propio.
        if (user.IsTerminal)
            return controller is "Orders" or "Products" or "ProductCategories" or "Branches" or "BranchMenu"
                or "DailyPromotions" or "LoyaltyCycleSteps" or "BranchPrintJobs" or "BranchPrintSettings"
                || controller == "EmployeeLunch" && action is "Branch" or "Start" or "Finish";
        return controller == "EmployeeLeaves"
            || controller == "BusinessDocuments" && action is "GetDocuments" or "GetDocument"
            || controller == "EmployeeLunch" && action is "Me" or "Start" or "Finish"
            || controller == "Users" && targetUserId == user.Id && action is "GetUser" or "UpdateProfile" or "UploadProfileImage"
            || controller == "Branches" && action == "GetBranchOptions";
    }
}
