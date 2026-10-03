using System.Xml;
using System.Xml.Linq;
using Careers.Application;
using Careers.Application.Offers;
using Careers.Domain.Offers;

namespace Careers.Infrastructure.Feeds;

/// <summary>We Work Remotely's RSS. Its terms: link back to the offer and name it as the source.</summary>
public sealed class WeWorkRemotelyFeed(HttpClient http) : IOfferFeed
{
    public string Source => "weworkremotely";

    public async Task<IReadOnlyList<JobOffer>> FetchAsync(DateTimeOffset now, CancellationToken ct) =>
        Parse(await FeedHttp.GetAsync(http, "https://weworkremotely.com/categories/remote-back-end-programming-jobs.rss", ct), now);

    public static IReadOnlyList<JobOffer> Parse(byte[] xml, DateTimeOffset now)
    {
        // DTDs are refused: an entity-expansion document is a classic way to exhaust memory from a "feed".
        using var reader = XmlReader.Create(new MemoryStream(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var offers = new List<JobOffer>();
        foreach (var item in XDocument.Load(reader).Descendants("item"))
        {
            var url = FeedHttp.SafeUrl(item.Element("link")?.Value.Trim());
            var full = item.Element("title")?.Value.Trim() ?? string.Empty;
            var id = item.Element("guid")?.Value.Trim() ?? url;
            if (url is null || string.IsNullOrWhiteSpace(full) || string.IsNullOrEmpty(id))
            {
                continue;
            }

            var split = full.IndexOf(": ", StringComparison.Ordinal);
            var (company, title) = split > 0 ? (full[..split], full[(split + 2)..]) : (string.Empty, full);
            DateTimeOffset? published = DateTimeOffset.TryParse(item.Element("pubDate")?.Value, out var p) ? p.ToUniversalTime() : null;
            offers.Add(JobOffer.Create(
                "weworkremotely", FeedHttp.Clip(id, 200), url, FeedHttp.Clip(title, 300), FeedHttp.Clip(company, 300),
                FeedHttp.Clip(item.Element("region")?.Value, 500), [], HtmlText.From(item.Element("description")?.Value), published, now));
        }

        return offers;
    }
}
