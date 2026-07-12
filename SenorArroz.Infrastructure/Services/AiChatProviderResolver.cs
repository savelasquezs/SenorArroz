using SenorArroz.Application.Common.Interfaces;
namespace SenorArroz.Infrastructure.Services;
public class AiChatProviderResolver(IEnumerable<IAiChatProvider> providers) : IAiChatProviderResolver { public IAiChatProvider? Resolve(string provider) => providers.FirstOrDefault(x => x.ProviderName.Equals(provider, StringComparison.OrdinalIgnoreCase)); }
