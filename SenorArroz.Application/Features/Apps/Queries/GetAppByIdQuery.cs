// SenorArroz.Application/Features/Apps/Queries/GetAppByIdQuery.cs
using MediatR;
using SenorArroz.Application.Features.Apps.DTOs;

namespace SenorArroz.Application.Features.Apps.Queries;

public class GetAppByIdQuery : IRequest<AppDto?>
{
    public int Id { get; set; }
}
