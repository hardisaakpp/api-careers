using System.Text.Json;
using Careers.Application;
using Careers.Application.Offers;
using Careers.Domain.Offers;

namespace Careers.Infrastructure.Feeds;

/// <summary>Get on Board's public API: the Latin American tech job board (mostly Spanish, on-site, hybrid and remote). Its terms: link back to the job page.</summary>
public sealed class GetOnBoardFeed(HttpClient http) : IOfferFeed
{
    private const int PerPage = 100;
    private const int Pages = 3;

    public string Source => "getonbrd";

    public async Task<IReadOnlyList<JobOffer>> FetchAsync(DateTimeOffset now, CancellationToken ct)
    {
        var offers = new List<JobOffer>();
        for (var page = 1; page <= Pages; page++)
        {
            var url = $"https://www.getonbrd.com/api/v0/categories/programming/jobs?per_page={PerPage}&page={page}&expand%5B%5D=company";
            var parsed = Parse(await FeedHttp.GetAsync(http, url, ct), now);
            offers.AddRange(parsed);
            if (parsed.Count < PerPage)
            {
                break;
            }
        }

        return offers;
    }

    public static IReadOnlyList<JobOffer> Parse(byte[] json, DateTimeOffset now)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var offers = new List<JobOffer>();
        foreach (var job in data.EnumerateArray())
        {
            if (!job.TryGetProperty("attributes", out var a) || a.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var id = FeedJson.Id(job, "id");
            var title = FeedJson.Text(a, "title");
            var url = FeedHttp.SafeUrl(job.TryGetProperty("links", out var links) ? FeedJson.Text(links, "public_url") : null);
            if (url is null || string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var description = string.Join("\n", new[] { "functions", "description", "desirable", "projects" }
                .Select(k => HtmlText.From(FeedJson.Text(a, k))).Where(t => t.Length > 0));
            offers.Add(JobOffer.Create(
                "getonbrd", FeedHttp.Clip(id, 200), url, FeedHttp.Clip(title, 300), FeedHttp.Clip(CompanyName(a), 300),
                FeedHttp.Clip(Place(a), 500), [], FeedHttp.Clip(description, 6000), FeedJson.Unix(a, "published_at"), now));
        }

        return offers;
    }

    private static string? CompanyName(JsonElement a) =>
        a.TryGetProperty("company", out var c) && c.TryGetProperty("data", out var d) && d.TryGetProperty("attributes", out var ca)
            ? FeedJson.Text(ca, "name")
            : null;

    private static string Place(JsonElement a)
    {
        var countries = FeedJson.Strings(a, "countries", 8);
        var modality = FeedJson.Text(a, "remote_modality") switch
        {
            "fully_remote" => "Remote",
            "hybrid" => "Hybrid",
            "no_remote" => "On-site",
            _ => null,
        };
        return string.Join(" · ", new[] { modality, string.Join(", ", countries) }.Where(s => !string.IsNullOrEmpty(s)));
    }
}
