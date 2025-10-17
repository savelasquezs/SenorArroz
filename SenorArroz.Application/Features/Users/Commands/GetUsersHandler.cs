// SenorArroz.Application/Features/Users/Queries/GetUsersHandler.cs
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Users.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Application.Features.Users.Queries;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.Users.Queries
{
    public class GetUsersHandler : IRequestHandler<GetUsersQuery, PagedResult<UserDto>>
    {
        private readonly IUserRepository _userRepository;
        private readonly IMapper _mapper;
        private readonly ICurrentUser _currentUser;
        private readonly ILogger<GetUsersHandler> _logger;

        public GetUsersHandler(IUserRepository userRepository, IMapper mapper, ICurrentUser currentUser, ILogger<GetUsersHandler> logger)
        {
            _userRepository = userRepository;
            _mapper = mapper;
            _currentUser = currentUser;
            _logger = logger;
        }

        public async Task<PagedResult<UserDto>> Handle(GetUsersQuery request, CancellationToken cancellationToken)
        {
            // Determinar filtro de sucursal según rol del usuario actual
            int? branchFilter = null;
            if (_currentUser.Role != "superadmin")
            {
                // Usuarios normales solo ven usuarios de su sucursal
                branchFilter = _currentUser.BranchId;
            }
            else if (request.BranchId.HasValue)
            {
                // Superadmin puede filtrar por sucursal específica
                branchFilter = request.BranchId;
            }

            // Obtener usuarios filtrados por sucursal
            var users = await _userRepository.GetAllAsync(branchFilter, cancellationToken);

            _logger.LogInformation("=== DEBUG GetUsers ===");
            _logger.LogInformation("Current User Role: {Role}, BranchId: {BranchId}", _currentUser.Role, _currentUser.BranchId);
            _logger.LogInformation("BranchFilter aplicado: {BranchFilter}", branchFilter);
            _logger.LogInformation("Total usuarios de repositorio: {Count}", users.Count());
            _logger.LogInformation("Filtro Role solicitado: {Role}", request.Role ?? "ninguno");
            _logger.LogInformation("Filtro Active solicitado: {Active}", request.Active?.ToString() ?? "ninguno");

            // Aplicar filtros adicionales
            var filteredUsers = users.AsEnumerable();

            // Filtrar por rol si se especifica
            if (!string.IsNullOrEmpty(request.Role))
            {
                filteredUsers = filteredUsers.Where(u => 
                    u.Role.HasValue && u.Role.Value.ToString().Equals(request.Role, StringComparison.OrdinalIgnoreCase));
            }

            // Filtrar por estado activo si se especifica
            if (request.Active.HasValue)
            {
                filteredUsers = filteredUsers.Where(u => u.Active == request.Active.Value);
            }

            var filteredList = filteredUsers.ToList();
            var totalCount = filteredList.Count;

            _logger.LogInformation("Total usuarios después de filtros: {Count}", totalCount);
            if (totalCount > 0)
            {
                _logger.LogInformation("Usuarios encontrados: {Users}", 
                    string.Join(", ", filteredList.Select(u => $"{u.Name} (Role: {u.Role}, Active: {u.Active})")));
            }

            // Aplicar paginación
            var paginatedUsers = filteredList
                .Skip((request.Page - 1) * request.PageSize)
                .Take(request.PageSize)
                .ToList();

            // Retornar resultado paginado
            return new PagedResult<UserDto>
            {
                Items = _mapper.Map<List<UserDto>>(paginatedUsers),
                TotalCount = totalCount,
                Page = request.Page,
                PageSize = request.PageSize,
                TotalPages = (int)Math.Ceiling((double)totalCount / request.PageSize)
            };
        }
    }

}
