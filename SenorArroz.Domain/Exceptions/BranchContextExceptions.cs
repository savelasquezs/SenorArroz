namespace SenorArroz.Domain.Exceptions;

public sealed class BranchContextRequiredException : Exception
{
    public const string ErrorCode = "branch_context_required";

    public BranchContextRequiredException()
        : base("Debes seleccionar una sucursal para realizar esta operación.")
    {
    }
}

public sealed class BranchScopeMismatchException : Exception
{
    public const string ErrorCode = "branch_scope_mismatch";

    public BranchScopeMismatchException()
        : base("El recurso solicitado no pertenece a la sucursal activa.")
    {
    }
}

public sealed class BranchAccessDeniedException : Exception
{
    public const string ErrorCode = "branch_access_denied";

    public BranchAccessDeniedException()
        : base("No tienes acceso a la sucursal solicitada.")
    {
    }
}
