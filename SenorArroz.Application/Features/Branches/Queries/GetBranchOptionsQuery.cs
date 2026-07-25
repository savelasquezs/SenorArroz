using MediatR;
using SenorArroz.Application.Features.Branches.DTOs;

namespace SenorArroz.Application.Features.Branches.Queries;

public sealed record GetBranchOptionsQuery : IRequest<IReadOnlyList<BranchOptionDto>>;
