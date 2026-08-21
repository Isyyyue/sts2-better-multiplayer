using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace BetterMultiplayer.Diagnostics;

internal enum FeedbackSendStatus
{
    Submitted,
    Busy,
    RateLimited,
    Rejected,
    NetworkFailed,
    Cancelled,
    InvalidPayload
}

internal sealed record FeedbackSendResult(
    FeedbackSendStatus Status,
    string EventId,
    HttpStatusCode? HttpStatus = null,
    TimeSpan? RetryAfter = null)
{
    internal bool Submitted => Status == FeedbackSendStatus.Submitted;
}

internal interface IFeedbackTransport
{
    Task<FeedbackSendResult> SendAsync(
        FeedbackEventPayload payload,
        CancellationToken cancellationToken);
}

internal sealed class SentryFeedbackTransport : IFeedbackTransport
{
    internal const string EnvelopeUrl =
        "https://o4511946758815744.ingest.us.sentry.io/api/4511946769170432/envelope/";
    internal const string PublicKey = "4bf752fde95cf045fa0143b30e4153a2";

    private static readonly HttpClient SharedClient = CreateClient();
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan DefaultRateLimit = TimeSpan.FromSeconds(60);

    internal static SentryFeedbackTransport Instance { get; } = new(
        SharedClient,
        new Uri(EnvelopeUrl),
        PublicKey,
        static () => DateTimeOffset.UtcNow,
        static (delay, cancellationToken) => Task.Delay(delay, cancellationToken));

    private readonly HttpClient _client;
    private readonly Uri _endpoint;
    private readonly string _publicKey;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly object _rateLimitGate = new();
    private DateTimeOffset _rateLimitedUntil;

    internal SentryFeedbackTransport(
        HttpClient client,
        Uri endpoint,
        string publicKey,
        Func<DateTimeOffset> clock,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        _client = client;
        _endpoint = endpoint;
        _publicKey = publicKey;
        _clock = clock;
        _delay = delay;
    }

    public async Task<FeedbackSendResult> SendAsync(
        FeedbackEventPayload payload,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = _clock();
        TimeSpan? cooldown = CurrentCooldown(now);
        if (cooldown.HasValue)
        {
            return new FeedbackSendResult(
                FeedbackSendStatus.RateLimited,
                payload.EventId,
                RetryAfter: cooldown);
        }

        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using CancellationTokenSource timeout =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(AttemptTimeout);
                using HttpRequestMessage request = CreateRequest(payload);
                using HttpResponseMessage response = await _client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token);

                now = _clock();
                TimeSpan? responseCooldown = ParseRateLimit(response, now);
                if (responseCooldown.HasValue)
                    SetCooldown(CooldownDeadline(now, responseCooldown.Value));

                if (response.IsSuccessStatusCode)
                {
                    return new FeedbackSendResult(
                        FeedbackSendStatus.Submitted,
                        payload.EventId,
                        response.StatusCode,
                        responseCooldown);
                }

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    TimeSpan retryAfter = responseCooldown ?? DefaultRateLimit;
                    SetCooldown(CooldownDeadline(now, retryAfter));
                    return new FeedbackSendResult(
                        FeedbackSendStatus.RateLimited,
                        payload.EventId,
                        response.StatusCode,
                        retryAfter);
                }

                return new FeedbackSendResult(
                    FeedbackSendStatus.Rejected,
                    payload.EventId,
                    response.StatusCode);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new FeedbackSendResult(FeedbackSendStatus.Cancelled, payload.EventId);
            }
            catch (HttpRequestException) when (attempt == 0)
            {
                if (!await DelayBeforeRetry(cancellationToken))
                    return new FeedbackSendResult(FeedbackSendStatus.Cancelled, payload.EventId);
            }
            catch (OperationCanceledException) when (attempt == 0)
            {
                if (!await DelayBeforeRetry(cancellationToken))
                    return new FeedbackSendResult(FeedbackSendStatus.Cancelled, payload.EventId);
            }
            catch (HttpRequestException)
            {
                return new FeedbackSendResult(FeedbackSendStatus.NetworkFailed, payload.EventId);
            }
            catch (OperationCanceledException)
            {
                return new FeedbackSendResult(FeedbackSendStatus.NetworkFailed, payload.EventId);
            }
        }

        return new FeedbackSendResult(FeedbackSendStatus.NetworkFailed, payload.EventId);
    }

    internal static TimeSpan? ParseRateLimit(
        HttpResponseMessage response,
        DateTimeOffset now)
    {
        double maximumSeconds = 0;
        if (response.Headers.TryGetValues("X-Sentry-Rate-Limits", out IEnumerable<string>? limits))
        {
            foreach (string header in limits)
            {
                foreach (string limit in header.Split(',', StringSplitOptions.TrimEntries))
                {
                    string[] parts = limit.Split(':');
                    if (parts.Length < 2 || !RelevantCategory(parts[1]))
                        continue;
                    if (double.TryParse(
                            parts[0],
                            NumberStyles.AllowDecimalPoint,
                            CultureInfo.InvariantCulture,
                            out double seconds) &&
                        double.IsFinite(seconds) &&
                        seconds > maximumSeconds)
                        maximumSeconds = seconds;
                }
            }
        }

        if (maximumSeconds > 0)
            return CooldownDuration(maximumSeconds, now);

        if (response.StatusCode != HttpStatusCode.TooManyRequests)
            return null;

        RetryConditionHeaderValue? retry = response.Headers.RetryAfter;
        if (retry?.Delta is { } delta && delta > TimeSpan.Zero)
            return Minimum(delta, DateTimeOffset.MaxValue - now);
        if (retry?.Date is { } date && date > now)
            return date - now;
        return null;
    }

    private static TimeSpan CooldownDuration(double seconds, DateTimeOffset now)
    {
        TimeSpan maximum = DateTimeOffset.MaxValue - now;
        return seconds >= maximum.TotalSeconds ? maximum : TimeSpan.FromSeconds(seconds);
    }

    private static DateTimeOffset CooldownDeadline(DateTimeOffset now, TimeSpan duration)
    {
        TimeSpan maximum = DateTimeOffset.MaxValue - now;
        return duration >= maximum ? DateTimeOffset.MaxValue : now + duration;
    }

    private static TimeSpan Minimum(TimeSpan left, TimeSpan right) =>
        left <= right ? left : right;

    private HttpRequestMessage CreateRequest(FeedbackEventPayload payload)
    {
        byte[] envelope = SentryEnvelopeSerializer.Serialize(payload, _clock());
        HttpRequestMessage request = new(HttpMethod.Post, _endpoint)
        {
            Content = new ByteArrayContent(envelope)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(
            "application/x-sentry-envelope");
        request.Headers.TryAddWithoutValidation(
            "X-Sentry-Auth",
            $"Sentry sentry_version=7, " +
            $"sentry_client=isyyyue.csharp.better-multiplayer/{BetterMultiplayerMod.Version}, " +
            $"sentry_key={_publicKey}");
        request.Headers.TryAddWithoutValidation(
            "User-Agent",
            $"better-multiplayer/{BetterMultiplayerMod.Version}");
        return request;
    }

    private async Task<bool> DelayBeforeRetry(CancellationToken cancellationToken)
    {
        try
        {
            await _delay(
                TimeSpan.FromMilliseconds(Random.Shared.Next(300, 701)),
                cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private TimeSpan? CurrentCooldown(DateTimeOffset now)
    {
        lock (_rateLimitGate)
            return _rateLimitedUntil > now ? _rateLimitedUntil - now : null;
    }

    private void SetCooldown(DateTimeOffset until)
    {
        lock (_rateLimitGate)
        {
            if (until > _rateLimitedUntil)
                _rateLimitedUntil = until;
        }
    }

    private static bool RelevantCategory(string value)
    {
        if (string.IsNullOrEmpty(value))
            return true;
        return value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(category => category is "error" or "default");
    }

    private static HttpClient CreateClient()
    {
        SocketsHttpHandler handler = new()
        {
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(3),
            MaxConnectionsPerServer = 1,
            UseCookies = false
        };
        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }
}

internal sealed class FeedbackUploadCoordinator
{
    private readonly IFeedbackTransport _transport;
    private int _active;

    internal FeedbackUploadCoordinator(IFeedbackTransport transport)
    {
        _transport = transport;
    }

    internal async Task<FeedbackSendResult> TrySendAsync(
        Func<FeedbackEventPayload> createPayload,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _active, 1, 0) != 0)
            return new FeedbackSendResult(FeedbackSendStatus.Busy, string.Empty);

        try
        {
            FeedbackEventPayload payload;
            try
            {
                payload = createPayload();
            }
            catch
            {
                return new FeedbackSendResult(FeedbackSendStatus.InvalidPayload, string.Empty);
            }

            return await _transport.SendAsync(payload, cancellationToken);
        }
        finally
        {
            Volatile.Write(ref _active, 0);
        }
    }
}

internal static class DiagnosticFeedbackService
{
    private static readonly FeedbackUploadCoordinator Coordinator = new(
        SentryFeedbackTransport.Instance);

    internal static async Task<FeedbackSendResult> SendAsync(
        Godot.Control source,
        CancellationToken cancellationToken = default)
    {
        FeedbackSendResult result = await Coordinator.TrySendAsync(() =>
        {
            DiagnosticRecorder.RecordFeedbackRequested();
            return FeedbackEventFactory.Create(
                DiagnosticRecorder.Snapshot(),
                DiagnosticSystemInfo.Capture(source),
                Guid.NewGuid(),
                DateTimeOffset.UtcNow);
        }, cancellationToken);

        string http = result.HttpStatus.HasValue
            ? ((int)result.HttpStatus.Value).ToString(CultureInfo.InvariantCulture)
            : "none";
        if (result.Submitted)
        {
            BetterMultiplayerMod.Logger.Info(
                $"Diagnostic feedback submitted: event={result.EventId}, http={http}");
        }
        else
        {
            BetterMultiplayerMod.Logger.Warn(
                $"Diagnostic feedback not submitted: event={result.EventId}, " +
                $"result={result.Status}, http={http}");
        }
        return result;
    }
}
