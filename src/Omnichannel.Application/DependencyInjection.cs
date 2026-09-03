using Microsoft.Extensions.DependencyInjection;
using Omnichannel.Application.Audit;
using Omnichannel.Application.Auth;
using Omnichannel.Application.Channels;
using Omnichannel.Application.Contacts;
using Omnichannel.Application.Conversations;
using Omnichannel.Application.Widget;

namespace Omnichannel.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<AuthService>();
        services.AddScoped<AuditService>();
        services.AddScoped<ContactService>();
        services.AddScoped<ConversationService>();
        services.AddScoped<TagService>();
        services.AddScoped<WidgetService>();
        services.AddScoped<WebhookIngestionService>();
        services.AddScoped<ChannelSendService>();
        return services;
    }
}
