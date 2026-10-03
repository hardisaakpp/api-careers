using Microsoft.Extensions.Logging;

namespace Careers.Application.Offers;

public sealed class OfferCaptureOptions
{
    /// <summary>The sources ask for few queries a day: no source is consulted more often than this.</summary>
    public TimeSpan MinInterval { get; set; } = TimeSpan.FromHours(6);
}

public enum FeedOutcome
{
    Fetched,
    Skipped,
    Failed,
}

public sealed record FeedResult(string Source, FeedOutcome Outcome, int Read, int Added);

public sealed class OfferCaptureService(
    IEnumerable<IOfferFeed> feeds,
    IOfferRepository offers,
    IFeedPollStore polls,
    OfferCaptureOptions options,
    TimeProvider clock,
    ILogger<OfferCaptureService> logger)
{
    private static readonly SemaphoreSlim _running = new(1, 1);

    /// <summary>Consults every source that is due. One failing source never stops the others.</summary>
    public async Task<IReadOnlyList<FeedResult>> RunAsync(bool force, CancellationToken ct)
    {
        // Two simultaneous refreshes would both see a source as due and query it twice.
        await _running.WaitAsync(ct);
        try
        {
            var results = new List<FeedResult>();
            foreach (var feed in feeds)
            {
                results.Add(await RunFeedAsync(feed, force, ct));
            }

            return results;
        }
        finally
        {
            _running.Release();
        }
    }

    private async Task<FeedResult> RunFeedAsync(IOfferFeed feed, bool force, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var last = await polls.LastAsync(feed.Source, ct);
        if (!force && last is not null && now - last < options.MinInterval)
        {
            return new FeedResult(feed.Source, FeedOutcome.Skipped, 0, 0);
        }

        try
        {
            var fetched = await feed.FetchAsync(now, ct);
            var added = await offers.AddNewAsync(fetched, ct);
            await polls.MarkAsync(feed.Source, now, ct);
            return new FeedResult(feed.Source, FeedOutcome.Fetched, fetched.Count, added);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Offer feed {Source} failed", feed.Source);
            return new FeedResult(feed.Source, FeedOutcome.Failed, 0, 0);
        }
    }
}
