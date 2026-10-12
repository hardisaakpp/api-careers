using System.Text.Json;
using Careers.Application;
using Careers.Application.Offers;
using Careers.Domain.Offers;

namespace Careers.Infrastructure.Feeds;

/// <summary>Himalayas' public API: remote jobs that accept someone living in Ecuador (no location restriction, or Ecuador among them). Its terms: link back to the job page.</summary>
public sealed class HimalayasFeed(HttpClient http) : IOfferFeed
{
    private const int PageSize = 20;
    private const int Pages = 3;
    private static readonly string[] Searches = ["software developer", "backend developer", "full stack developer"];

    public string Source => "himalayas";

    public async Task<IReadOnlyList<JobOffer>> FetchAsync(DateTimeOffset now, CancellationToken ct)
    {
        var offers = new Dictionary<string, JobOffer>();
        foreach (var search in Searches)
        {
            for (var page = 0; page < Pages; page++)
            {
                var url = $"https://himalayas.app/jobs/api/search?q={Uri.EscapeDataString(search)}&country=Ecuador&limit={PageSize}&offset={page * PageSize}";
                var parsed = Parse(await FeedHttp.GetAsync(http, url, ct), now);
                foreach (var offer in parsed)
                {
                    offers.TryAdd(offer.SourceId, offer);
                }

                if (parsed.Count < PageSize)
                {
                    break;
                }
            }
        }

        return offers.Values.ToList();
    }

    public static IReadOnlyList<JobOffer> Parse(byte[] json, DateTimeOffset now)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("jobs", out var jobs) || jobs.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var offers = new List<JobOffer>();
        foreach (var job in jobs.EnumerateArray())
        {
            var url = FeedHttp.SafeUrl(FeedJson.Text(job, "applicationLink") ?? FeedJson.Text(job, "guid"));
            var title = FeedJson.Text(job, "title");
            // The page URL is the stable identity: the feed has no numeric id.
            var id = FeedJson.Text(job, "guid") ?? url;
            if (url is null || string.IsNullOrWhiteSpace(title) || string.IsNullOrEmpty(id))
            {
                continue;
            }

            var places = FeedJson.Strings(job, "locationRestrictions", 6).ToList();
            var tags = FeedJson.Strings(job, "categories").Concat(FeedJson.Strings(job, "seniority", 5));
            offers.Add(JobOffer.Create(
                "himalayas", FeedHttp.Clip(id, 200), url, FeedHttp.Clip(title, 300), FeedHttp.Clip(FeedJson.Text(job, "companyName"), 300),
                places.Count == 0 ? "Worldwide" : FeedHttp.Clip(string.Join(", ", places), 500), tags,
                HtmlText.From(FeedJson.Text(job, "description") ?? FeedJson.Text(job, "excerpt")),
                FeedJson.Unix(job, "pubDate"), now));
        }

        return offers;
    }
}
