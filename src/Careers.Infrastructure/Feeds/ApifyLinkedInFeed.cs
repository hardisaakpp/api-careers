using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Careers.Application;
using Careers.Application.Offers;
using Careers.Domain.Offers;
using Microsoft.Extensions.Options;

namespace Careers.Infrastructure.Feeds;

/// <summary>Where the Apify account and the LinkedIn searches come from. Without a token the feed is not registered at all.</summary>
public sealed class ApifyOptions
{
    public string Token { get; set; } = string.Empty;

    /// <summary>Actor id as Apify writes it in URLs (<c>user~name</c>).</summary>
    public string Actor { get; set; } = "curious_coder~linkedin-jobs-scraper";

    /// <summary>LinkedIn public job-search URLs, one run each. Each costs Apify credits.</summary>
    public List<string> SearchUrls { get; set; } =
    [
        "https://www.linkedin.com/jobs/search/?keywords=desarrollador%20software&location=Ecuador&f_TPR=r604800",
        "https://www.linkedin.com/jobs/search/?keywords=software%20developer&location=Ecuador&f_TPR=r604800&f_WT=2",
        "https://www.linkedin.com/jobs/search/?keywords=software%20developer&location=United%20States&f_TPR=r604800&f_WT=2",
    ];

    public int MaxItemsPerSearch { get; set; } = 40;
}

/// <summary>
/// LinkedIn offers through an Apify actor that reads LinkedIn's public (logged-out) job pages. LinkedIn's terms forbid
/// automated collection, so this is opt-in and the risk is the account owner's; no LinkedIn credentials are ever used.
/// </summary>
public sealed class ApifyLinkedInFeed(HttpClient http, IOptions<ApifyOptions> options) : IOfferFeed
{
    public string Source => "linkedin";

    public async Task<IReadOnlyList<JobOffer>> FetchAsync(DateTimeOffset now, CancellationToken ct)
    {
        var settings = options.Value;
        var input = JsonSerializer.Serialize(new
        {
            urls = settings.SearchUrls,
            count = Math.Max(1, settings.MaxItemsPerSearch * Math.Max(1, settings.SearchUrls.Count)),
            scrapeCompany = false,
        });
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://api.apify.com/v2/acts/{Uri.EscapeDataString(settings.Actor)}/run-sync-get-dataset-items")
        {
            Content = new StringContent(input, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > FeedHttp.MaxBytes)
        {
            throw new InvalidOperationException("The feed is too large.");
        }

        return Parse(await response.Content.ReadAsByteArrayAsync(ct), now);
    }

    /// <summary>Actors name their fields differently; the usual spellings are all tried.</summary>
    public static IReadOnlyList<JobOffer> Parse(byte[] json, DateTimeOffset now)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var offers = new Dictionary<string, JobOffer>();
        foreach (var job in doc.RootElement.EnumerateArray())
        {
            var url = FeedHttp.SafeUrl(First(job, "link", "jobUrl", "url"));
            var title = First(job, "title", "jobTitle", "position");
            var id = FeedJson.Id(job, "id") ?? FeedJson.Id(job, "jobId") ?? url;
            if (url is null || string.IsNullOrWhiteSpace(title) || string.IsNullOrEmpty(id))
            {
                continue;
            }

            var text = First(job, "descriptionText", "description") ?? First(job, "descriptionHtml");
            offers.TryAdd(id, JobOffer.Create(
                "linkedin", FeedHttp.Clip(id, 200), url, FeedHttp.Clip(title, 300),
                FeedHttp.Clip(First(job, "companyName", "company"), 300), FeedHttp.Clip(First(job, "location", "jobLocation"), 500),
                FeedJson.Strings(job, "skills", 20), HtmlText.From(text),
                FeedJson.Date(First(job, "postedAt", "publishedAt", "postedDate")), now));
        }

        return offers.Values.ToList();
    }

    private static string? First(JsonElement e, params string[] names) =>
        names.Select(n => FeedJson.Text(e, n)).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
