using System.Net;
using System.Text;
using System.Text.Json;
using BetterMultiplayer.Diagnostics;

namespace BetterMultiplayer.Tests;

public sealed class SentryFeedbackTransportTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-08-21T04:00:00Z");

    [Fact]
    public async Task AcceptedEnvelopeIsReportedAsSubmitted()
    {
        RecordingHandler handler = new((_, _, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted)));
        SentryFeedbackTransport transport = CreateTransport(handler);
        FeedbackEventPayload payload = Payload();

        FeedbackSendResult result = await transport.SendAsync(payload, CancellationToken.None);

        Assert.True(result.Submitted);
        Assert.Equal(payload.EventId, result.EventId);
        Assert.Equal(HttpStatusCode.Accepted, result.HttpStatus);
        Assert.Single(handler.Requests);
        RecordedRequest request = handler.Requests[0];
        Assert.Equal(SentryFeedbackTransport.EnvelopeUrl, request.Uri);
        Assert.Equal("application/x-sentry-envelope", request.ContentType);
        Assert.Contains(
            $"sentry_key={SentryFeedbackTransport.PublicKey}",
            request.Authorization,
            StringComparison.Ordinal);
        using JsonDocument header = JsonDocument.Parse(
            Encoding.UTF8.GetString(request.Body).Split('\n')[0]);
        Assert.Equal(payload.EventId, header.RootElement.GetProperty("event_id").GetString());
    }

    [Fact]
    public async Task TransportFailureRetriesOnceWithTheSameEventId()
    {
        RecordingHandler handler = new((_, attempt, _) =>
        {
            if (attempt == 1)
                throw new HttpRequestException("simulated");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        int delays = 0;
        SentryFeedbackTransport transport = CreateTransport(
            handler,
            (_, _) =>
            {
                delays++;
                return Task.CompletedTask;
            });
        FeedbackEventPayload payload = Payload();

        FeedbackSendResult result = await transport.SendAsync(payload, CancellationToken.None);

        Assert.True(result.Submitted);
        Assert.Equal(2, handler.Attempts);
        Assert.Equal(1, delays);
        string firstId = EnvelopeId(handler.Requests[0].Body);
        string secondId = EnvelopeId(handler.Requests[1].Body);
        Assert.Equal(payload.EventId, firstId);
        Assert.Equal(firstId, secondId);
    }

    [Fact]
    public async Task HttpFailureIsNotRetried()
    {
        RecordingHandler handler = new((_, _, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        SentryFeedbackTransport transport = CreateTransport(handler);

        FeedbackSendResult result = await transport.SendAsync(Payload(), CancellationToken.None);

        Assert.Equal(FeedbackSendStatus.Rejected, result.Status);
        Assert.Equal(1, handler.Attempts);
    }

    [Fact]
    public async Task ServiceUnavailableRetryAfterDoesNotBecomeRateLimit()
    {
        RecordingHandler handler = new((_, _, _) =>
        {
            HttpResponseMessage response = new(HttpStatusCode.ServiceUnavailable);
            response.Headers.TryAddWithoutValidation("Retry-After", "120");
            return Task.FromResult(response);
        });
        SentryFeedbackTransport transport = CreateTransport(handler);

        FeedbackSendResult first = await transport.SendAsync(Payload(), CancellationToken.None);
        FeedbackSendResult second = await transport.SendAsync(Payload(), CancellationToken.None);

        Assert.Equal(FeedbackSendStatus.Rejected, first.Status);
        Assert.Equal(FeedbackSendStatus.Rejected, second.Status);
        Assert.Equal(2, handler.Attempts);
    }

    [Fact]
    public async Task RateLimitPreventsAnotherRequestDuringCooldown()
    {
        RecordingHandler handler = new((_, _, _) =>
        {
            HttpResponseMessage response = new(HttpStatusCode.TooManyRequests);
            response.Headers.TryAddWithoutValidation("X-Sentry-Rate-Limits", "120:error:organization");
            return Task.FromResult(response);
        });
        SentryFeedbackTransport transport = CreateTransport(handler);

        FeedbackSendResult first = await transport.SendAsync(Payload(), CancellationToken.None);
        FeedbackSendResult second = await transport.SendAsync(Payload(), CancellationToken.None);

        Assert.Equal(FeedbackSendStatus.RateLimited, first.Status);
        Assert.Equal(TimeSpan.FromSeconds(120), first.RetryAfter);
        Assert.Equal(FeedbackSendStatus.RateLimited, second.Status);
        Assert.Equal(1, handler.Attempts);
    }

    [Fact]
    public void TooManyRequestsUsesRetryAfterWhenSentryHeaderIsMissing()
    {
        using HttpResponseMessage response = new(HttpStatusCode.TooManyRequests);
        response.Headers.TryAddWithoutValidation("Retry-After", "90");

        TimeSpan? cooldown = SentryFeedbackTransport.ParseRateLimit(response, Now);

        Assert.Equal(TimeSpan.FromSeconds(90), cooldown);
    }

    [Fact]
    public void SentryRateLimitIsNotTruncatedToOneDay()
    {
        using HttpResponseMessage response = new(HttpStatusCode.TooManyRequests);
        response.Headers.TryAddWithoutValidation(
            "X-Sentry-Rate-Limits",
            "172800:error:organization");

        TimeSpan? cooldown = SentryFeedbackTransport.ParseRateLimit(response, Now);

        Assert.Equal(TimeSpan.FromDays(2), cooldown);
    }

    [Fact]
    public async Task UploadCoordinatorAllowsOnlyOneInFlightRequest()
    {
        BlockingTransport transport = new();
        FeedbackUploadCoordinator coordinator = new(transport);
        int payloadsCreated = 0;
        Func<FeedbackEventPayload> factory = () =>
        {
            payloadsCreated++;
            return Payload();
        };

        Task<FeedbackSendResult> first = coordinator.TrySendAsync(factory);
        await transport.Started.Task;
        FeedbackSendResult concurrent = await coordinator.TrySendAsync(factory);
        transport.Release.SetResult();
        FeedbackSendResult completed = await first;
        FeedbackSendResult afterRelease = await coordinator.TrySendAsync(factory);

        Assert.Equal(FeedbackSendStatus.Busy, concurrent.Status);
        Assert.True(completed.Submitted);
        Assert.True(afterRelease.Submitted);
        Assert.Equal(2, payloadsCreated);
    }

    private static SentryFeedbackTransport CreateTransport(
        RecordingHandler handler,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        HttpClient client = new(handler) { Timeout = Timeout.InfiniteTimeSpan };
        return new SentryFeedbackTransport(
            client,
            new Uri(SentryFeedbackTransport.EnvelopeUrl),
            SentryFeedbackTransport.PublicKey,
            () => Now,
            delay ?? ((_, _) => Task.CompletedTask));
    }

    private static FeedbackEventPayload Payload() => new(
        "fc6d8c0c43fc4630ad850ee518f1b9d0",
        Now,
        Encoding.UTF8.GetBytes(
            "{\"event_id\":\"fc6d8c0c43fc4630ad850ee518f1b9d0\"}"));

    private static string EnvelopeId(byte[] body)
    {
        string header = Encoding.UTF8.GetString(body).Split('\n')[0];
        using JsonDocument document = JsonDocument.Parse(header);
        return document.RootElement.GetProperty("event_id").GetString()!;
    }

    private sealed record RecordedRequest(
        string Uri,
        string ContentType,
        string Authorization,
        byte[] Body);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> _response;

        internal RecordingHandler(
            Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> response)
        {
            _response = response;
        }

        internal int Attempts { get; private set; }
        internal List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Attempts++;
            byte[] body = request.Content is null
                ? []
                : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            Requests.Add(new RecordedRequest(
                request.RequestUri!.AbsoluteUri,
                request.Content?.Headers.ContentType?.MediaType ?? string.Empty,
                request.Headers.TryGetValues("X-Sentry-Auth", out IEnumerable<string>? values)
                    ? string.Join(",", values)
                    : string.Empty,
                body));
            return await _response(request, Attempts, cancellationToken);
        }
    }

    private sealed class BlockingTransport : IFeedbackTransport
    {
        private int _calls;
        internal TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<FeedbackSendResult> SendAsync(
            FeedbackEventPayload payload,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                Started.SetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return new FeedbackSendResult(FeedbackSendStatus.Submitted, payload.EventId);
        }
    }
}
