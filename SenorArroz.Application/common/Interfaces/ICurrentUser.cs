namespace SenorArroz.Application.Common.Interfaces
{
    public interface ICurrentUser
    {
        int Id { get; }         // Id del usuario logueado
        string Role { get; }       // Rol del usuario (superadmin, admin, etc.)
        int BranchId { get; }      // Id de la sucursal del usuario (si aplica)
        bool IsAuthenticated { get; } // Indica si hay un usuario logueado
    }
}