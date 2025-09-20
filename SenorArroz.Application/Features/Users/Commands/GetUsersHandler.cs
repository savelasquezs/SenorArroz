// SenorArroz.Application/Features/Users/Queries/GetUsersHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.Users.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Application.Features.Users.Queries;

namespace SenorArroz.Application.Features.Users.Commands
{
    public class GetUsersHandler : IRequestHandler<GetUsersQuery, IEnumerable<UserDto>>
    {
        private readonly IUserRepository _userRepository;
        private readonly IMapper _mapper;

        public GetUsersHandler(IUserRepository userRepository, IMapper mapper)
        {
            _userRepository = userRepository;
            _mapper = mapper;
        }

        public async Task<IEnumerable<UserDto>> Handle(GetUsersQuery request, CancellationToken cancellationToken)
        {
            // Obtener usuarios, opcionalmente filtrados por sucursal
            var users = await _userRepository.GetAllAsync(request.BranchId, cancellationToken);

            // Mapear a DTOs
            return _mapper.Map<IEnumerable<UserDto>>(users);
        }
    }

}
