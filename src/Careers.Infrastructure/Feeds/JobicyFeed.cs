using System.Text.Json;
using Careers.Application;
using Careers.Application.Offers;
using Careers.Domain.Offers;

namespace Careers.Infrastructure.Feeds;

/// <summary>Jobicy's public API, asked for remote dev jobs open to Latin America and to the USA. Its terms: credit Jobicy and send the applicant to the original URL.</summary>
public sealed class JobicyFeed(HttpClient http) : IOfferFeed
{
    private static readonly string[] Regions = ["latam", "usa"];

    public string Source => "jobicy";

    public async Task<IReadOnlyList<JobOffer>> FetchAsync(DateTimeOffset now, CancellationToken ct)
    {
        var offers = new Dictionary<string, JobOffer>();
        foreach (var region in Regions)
        {
            var bytes = await FeedHttp.GetAsync(http, $"https://jobicy.com/api/v2/remote-jobs?count=50&industry=dev&geo={region}", ct);
            foreach (var offer in Parse(bytes, now))
            {
                offers.TryAdd(offer.SourceId, offer);
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
            var url = FeedHttp.SafeUrl(FeedJson.Text(job, "url"));
            var title = FeedJson.Text(job, "jobTitle");
            var id = FeedJson.Id(job, "id");
            if (url is null || string.IsNullOrWhiteSpace(title) || string.IsNullOrEmpty(id))
            {
                continue;
            }

            var tags = FeedJson.Strings(job, "jobIndustry").Concat(FeedJson.Strings(job, "jobType"));
            var level = FeedJson.Text(job, "jobLevel");
            offers.Add(JobOffer.Create(
                "jobicy", id, url, FeedHttp.Clip(title, 300), FeedHttp.Clip(FeedJson.Text(job, "companyName"), 300),
                FeedHttp.Clip(FeedJson.Text(job, "jobGeo"), 500), string.IsNullOrWhiteSpace(level) ? tags : tags.Append(FeedHttp.Clip(level, 60)),
                HtmlText.From(FeedJson.Text(job, "jobDescription") ?? FeedJson.Text(job, "jobExcerpt")),
                FeedJson.Date(FeedJson.Text(job, "pubDate")), now));
        }

        return offers;
    }
}
