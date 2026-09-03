using Omnichannel.Application.Abstractions;
using Omnichannel.Domain.Channels;

namespace Omnichannel.ApiTests.Channels;

/// <summary>
/// Exercises the Phase 6 webhook/send pipeline end-to-end without a real provider — registered
/// only in test DI (see ChannelWebhookEndpointsTests), standing in for the WhatsApp slot that
/// Phase 7 will fill with a real adapter. Behavior is fully controlled by the test via the public
/// mutable fields, so each test configures exactly the scenario it needs.
/// </summary>
public sealed class FakeChannelAdapter : IChannelAdapter
{
    public ChannelType Type => ChannelType.WhatsApp;

    public ChannelCapabilities Capabilities { get; } = new(4096, SupportsMedia: true, SupportsDeliveryReceipts: true, SupportsReadReceipts: true, HasMessagingWindow: true);

    public bool VerifyShouldSucceed { get; set; } = true;

    public string? ChallengeResponse { get; set; }

    public List<NormalizedInboundEvent> EventsToReturn { get; } = [];

    /// <summary>Queue of results TrySendAsync consumes in order — lets a test simulate "fails twice, then succeeds".</summary>
    public Queue<ChannelSendResult> SendResultQueue { get; } = new();

    public int SendCallCount { get; private set; }

    public Task<WebhookVerificationResult> VerifyWebhookAsync(WebhookRequest request, CancellationToken cancellationToken)
        => Task.FromResult(VerifyShouldSucceed
            ? WebhookVerificationResult.Valid(ChallengeResponse)
            : WebhookVerificationResult.Invalid("Signature mismatch."));

    public Task<IReadOnlyList<NormalizedInboundEvent>> ParseWebhookAsync(WebhookRequest request, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<NormalizedInboundEvent>>(EventsToReturn);

    public Task<ChannelSendResult> SendMessageAsync(ChannelSendRequest request, CancellationToken cancellationToken)
    {
        SendCallCount++;
        var result = SendResultQueue.Count > 0 ? SendResultQueue.Dequeue() : ChannelSendResult.Ok(Guid.NewGuid().ToString());
        if (result.Success)
        {
            return Task.FromResult(result);
        }

        return result.ErrorKind is ChannelSendErrorKind.Transient or ChannelSendErrorKind.RateLimited
            ? throw new ChannelSendException(result.ErrorKind.Value, result.ErrorDetail ?? "transient failure")
            : Task.FromResult(result);
    }
}
