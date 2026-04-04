using MediatR;
using SenorArroz.Application.Features.Users.DTOs;

namespace SenorArroz.Application.Features.Users.Queries;

public record GetUserPayrollInsightsQuery(
    int UserId,
    string From,
    string To,
    string SeriesGranularity
) : IRequest<UserPayrollInsightsDto>;
