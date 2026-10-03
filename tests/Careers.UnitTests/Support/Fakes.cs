using Careers.Application;
using Careers.Domain.Cv;
using Careers.Domain.Offers;

namespace Careers.UnitTests.Support;

public sealed class TestClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>Deterministic stand-in for careers-ai: "backend" titles score high, "sales" titles are excluded.</summary>
public sealed class FakeAi : ICareersAi
{
    public bool Down { get; set; }
    public int Pending { get; set; }
    public int AdaptCalls { get; private set; }
    public CvDocument? LastRendered { get; private set; }
    public SearchPreferences? LastPreferences { get; private set; }

    private void Check()
    {
        if (Down)
        {
            throw new AiUnavailableException("The AI service is unreachable.");
        }
    }

    public Task<RankResult> RankAsync(CvDocument cv, SearchPreferences preferences, IReadOnlyList<JobOffer> offers, CancellationToken ct)
    {
        Check();
        LastPreferences = preferences;
        var ranked = offers.Select(o => new RankedOffer(
            o.Id,
            o.Title.Contains("backend", StringComparison.OrdinalIgnoreCase) ? 90 : 20,
            o.Title.Contains("sales", StringComparison.OrdinalIgnoreCase) ? "title excluded" : null,
            o.Description.Contains(".NET") ? [".NET"] : [])).ToList();
        return Task.FromResult(new RankResult(Pending, ranked));
    }

    public Task<CvDocument> AdaptAsync(CvDocument cv, JobOffer offer, CancellationToken ct)
    {
        Check();
        AdaptCalls++;
        return Task.FromResult(cv with { Headline = cv.Headline + " (tailored)" });
    }

    public Task<string> LetterAsync(CvDocument cv, JobOffer offer, CancellationToken ct)
    {
        Check();
        return Task.FromResult($"Dear {offer.Company}: I am interested in {offer.Title}.");
    }

    public Task<byte[]> RenderPdfAsync(CvDocument cv, CancellationToken ct)
    {
        Check();
        LastRendered = cv;
        return Task.FromResult("%PDF-fake"u8.ToArray());
    }
}

public sealed class FakeFeed(string source) : IOfferFeed
{
    public string Source { get; } = source;
    public bool Fail { get; set; }
    public int Calls { get; private set; }
    public List<(string Id, string Title, string Company)> Offers { get; } = [];

    public Task<IReadOnlyList<JobOffer>> FetchAsync(DateTimeOffset now, CancellationToken ct)
    {
        Calls++;
        if (Fail)
        {
            throw new HttpRequestException("boom");
        }

        IReadOnlyList<JobOffer> offers = Offers.Select(o => JobOffer.Create(
            Source, o.Id, $"https://{Source}.example/{o.Id}", o.Title, o.Company, "Worldwide", ["dotnet"],
            "Build APIs with .NET", now, now)).ToList();
        return Task.FromResult(offers);
    }
}
