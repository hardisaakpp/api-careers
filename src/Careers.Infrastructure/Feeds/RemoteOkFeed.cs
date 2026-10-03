using System.Text.Json;
using Careers.Application;
using Careers.Application.Offers;
using Careers.Domain.Offers;

namespace Careers.Infrastructure.Feeds;

/// <summary>Remote OK's public API. Its terms: link back to the offer and name Remote OK as the source.</summary>
public sealed class RemoteOkFeed(HttpClient http) : IOfferFeed
{
    public string Source => "remoteok";

    public async Task<IReadOnlyList<JobOffer>> FetchAsync(DateTimeOffset now, CancellationToken ct) =>
        Parse(await FeedHttp.GetAsync(http, "https://remoteok.com/api", ct), now);

    public static IReadOnlyList<JobOffer> Parse(byte[] json, DateTimeOffset now)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var offers = new List<JobOffer>();
        foreach (var job in doc.RootElement.EnumerateArray())
        {
            // The first element is a legal notice, not an offer.
            if (job.ValueKind != JsonValueKind.Object || !job.TryGetProperty("position", out _))
            {
                continue;
            }

            var url = FeedHttp.SafeUrl(Text(job, "url"));
            var title = Text(job, "position");
            var id = job.TryGetProperty("id", out var idValue) ? idValue.ToString() : null;
            if (url is null || string.IsNullOrWhiteSpace(title) || string.IsNullOrEmpty(id))
            {
                continue;
            }

            DateTimeOffset? published = DateTimeOffset.TryParse(Text(job, "date"), out var p) ? p : null;
            var tags = job.TryGetProperty("tags", out var t) && t.ValueKind == JsonValueKind.Array
                ? t.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => FeedHttp.Clip(x.GetString(), 60)).Take(40)
                : [];
            offers.Add(JobOffer.Create(
                "remoteok", id, url, FeedHttp.Clip(title, 300), FeedHttp.Clip(Text(job, "company"), 300),
                FeedHttp.Clip(Text(job, "location"), 500), tags, HtmlText.From(Text(job, "description")), published, now));
        }

        return offers;
    }

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
