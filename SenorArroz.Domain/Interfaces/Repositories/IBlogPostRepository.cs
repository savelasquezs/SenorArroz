using SenorArroz.Domain.Entities;

namespace SenorArroz.Domain.Interfaces.Repositories;

public interface IBlogPostRepository
{
    Task<IReadOnlyList<BlogPost>> GetPublishedAsync(CancellationToken cancellationToken = default);
    Task<BlogPost?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task<BlogPost> UpsertAsync(BlogPost post, CancellationToken cancellationToken = default);
}
