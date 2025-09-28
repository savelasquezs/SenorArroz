using MediatR;
using SenorArroz.Application.Features.Users.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SenorArroz.Application.Features.Users.Commands
{
    public class ToggleStatusCommand(int id) : IRequest<UserDto>
    {
        public int Id { get; set; } = id;
    }
}
