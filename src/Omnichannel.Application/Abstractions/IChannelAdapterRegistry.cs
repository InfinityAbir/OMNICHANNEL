using Omnichannel.Domain.Channels;

namespace Omnichannel.Application.Abstractions;

/// <summary>Resolves the registered <see cref="IChannelAdapter"/> for a channel type, if any. Returns null for Manual/WebsiteChat (no adapter — see IChannelAdapter's own remarks) and for any channel whose Phase hasn't shipped yet.</summary>
public interface IChannelAdapterRegistry
{
    IChannelAdapter? Resolve(ChannelType type);
}
