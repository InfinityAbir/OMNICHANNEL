namespace Omnichannel.Domain.Channels;

/// <summary>
/// The channel taxonomy from PRD §7. Only <see cref="Manual"/> has a working adapter this
/// phase — the rest are named now so Conversation/Message can reference a stable enum, but no
/// behavior for them exists until their own phase (5 for WebsiteChat, 7-9 for WhatsApp/
/// Instagram/Messenger, later for Telegram/Email).
/// </summary>
public enum ChannelType
{
    Manual = 0,
    WebsiteChat = 1,
    WhatsApp = 2,
    Instagram = 3,
    Messenger = 4,
    Telegram = 5,
    Email = 6,
}
