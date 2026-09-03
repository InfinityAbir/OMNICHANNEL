using Microsoft.Extensions.Logging;
using Omnichannel.Application.Abstractions;
using Omnichannel.Domain.Channels;
using Omnichannel.Domain.Conversations;
using Polly;
using Polly.Retry;

namespace Omnichannel.Application.Channels;

/// <summary>
/// Outbound routing + retry architecture (PRD §65). Only <see cref="ChannelSendErrorKind.Transient"/>
/// and <see cref="ChannelSendErrorKind.RateLimited"/> are retried — an auth failure or invalid
/// recipient will never succeed on retry, so those fail fast instead of wasting attempts and
/// delaying the agent's feedback. Manual/WebsiteChat have no registered adapter, so
/// <see cref="TrySendAsync"/> returns null for them and the caller keeps its existing
/// immediate-send behavior unchanged.
/// </summary>
public sealed partial class ChannelSendService(IChannelAdapterRegistry registry, IChannelCredentialStore credentials, ILogger<ChannelSendService> logger)
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbound send to {ChannelType} failed permanently: {ErrorKind}")]
    private static partial void LogSendFailedPermanently(ILogger logger, Exception exception, ChannelType channelType, ChannelSendErrorKind errorKind);

    private static readonly ResiliencePipeline<ChannelSendResult> RetryPipeline = new ResiliencePipelineBuilder<ChannelSendResult>()
        .AddRetry(new RetryStrategyOptions<ChannelSendResult>
        {
            ShouldHandle = new PredicateBuilder<ChannelSendResult>()
                .Handle<ChannelSendException>(ex => ex.ErrorKind is ChannelSendErrorKind.Transient or ChannelSendErrorKind.RateLimited)
                .HandleResult(r => !r.Success && r.ErrorKind is ChannelSendErrorKind.Transient or ChannelSendErrorKind.RateLimited),
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromMilliseconds(200),
        })
        .Build();

    /// <summary>Attempts a provider send if this channel has a registered adapter. Returns null when it doesn't (Manual/WebsiteChat, or a channel type whose phase hasn't shipped) — the caller should fall back to its own behavior.</summary>
    public async Task<ChannelSendResult?> TrySendAsync(ChannelAccount account, string recipientExternalId, string text, CancellationToken cancellationToken)
    {
        var adapter = registry.Resolve(account.Type);
        if (adapter is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(account.ExternalAccountId))
        {
            return ChannelSendResult.Failed(ChannelSendErrorKind.PermanentFailure, "Channel account is not connected to a provider.");
        }

        var secret = await credentials.GetAsync(account.Id, cancellationToken);
        if (secret is null)
        {
            return ChannelSendResult.Failed(ChannelSendErrorKind.AuthFailed, "No credential configured for this channel account.");
        }

        var request = new ChannelSendRequest(account.Id, account.ExternalAccountId, recipientExternalId, text, secret);

        try
        {
            return await RetryPipeline.ExecuteAsync(async ct => await adapter.SendMessageAsync(request, ct), cancellationToken);
        }
        catch (ChannelSendException ex)
        {
            LogSendFailedPermanently(logger, ex, account.Type, ex.ErrorKind);
            return ChannelSendResult.Failed(ex.ErrorKind, ex.Message);
        }
    }
}
