using Omnichannel.Application.Abstractions;
using Omnichannel.Domain.Channels;

namespace Omnichannel.Infrastructure.Channels;

/// <summary>Built from whatever IChannelAdapter implementations are registered in DI — empty in production until Phase 7 registers WhatsApp's. Tests register a fake adapter to exercise the pipeline end-to-end without a real provider.</summary>
public sealed class ChannelAdapterRegistry(IEnumerable<IChannelAdapter> adapters) : IChannelAdapterRegistry
{
    // Last registration for a given type wins — lets test hosts override a production adapter by
    // registering a fake one later in ConfigureTestServices, without needing to also remove the
    // production registration first.
    private readonly IReadOnlyDictionary<ChannelType, IChannelAdapter> _byType =
        adapters.GroupBy(a => a.Type).ToDictionary(g => g.Key, g => g.Last());

    public IChannelAdapter? Resolve(ChannelType type) => _byType.GetValueOrDefault(type);
}
