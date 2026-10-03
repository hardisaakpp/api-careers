using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Careers.Domain.Offers;

/// <summary>A published vacancy. Its text comes from third parties and is never treated as instructions.</summary>
public sealed class JobOffer
{
    private JobOffer() { }

    public Guid Id { get; private set; }
    public string Source { get; private set; } = string.Empty;
    public string SourceId { get; private set; } = string.Empty;
    /// <summary>Link back to where it was published: the sources require it, and it is where the person applies.</summary>
    public string Url { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Company { get; private set; } = string.Empty;
    public string Location { get; private set; } = string.Empty;
    public IReadOnlyList<string> Tags { get; private set; } = [];
    public string Description { get; private set; } = string.Empty;
    public DateTimeOffset? PublishedAt { get; private set; }
    public DateTimeOffset FetchedAt { get; private set; }
    /// <summary>Same company + title, whichever source published it.</summary>
    public string DedupKey { get; private set; } = string.Empty;
    /// <summary>Other sources that published the same vacancy (comma separated, sorted).</summary>
    public string AlsoOn { get; private set; } = string.Empty;

    public static JobOffer Create(
        string source, string sourceId, string url, string title, string company, string location,
        IEnumerable<string> tags, string description, DateTimeOffset? publishedAt, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        Source = source,
        SourceId = sourceId,
        Url = url,
        Title = title.Trim(),
        Company = company.Trim(),
        Location = location.Trim(),
        Tags = tags.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        Description = description,
        PublishedAt = publishedAt,
        FetchedAt = now,
        DedupKey = KeyFor(company, title),
    };

    public void AlsoPublishedOn(string source)
    {
        if (source == Source)
        {
            return;
        }

        AlsoOn = string.Join(',', AlsoOn.Split(',', StringSplitOptions.RemoveEmptyEntries).Append(source)
            .Distinct().Order());
    }

    public static string KeyFor(string company, string title)
    {
        var text = $"{Normalize(company)}|{Normalize(title)}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16].ToLowerInvariant();
    }

    private static string Normalize(string text) =>
        Regex.Replace(text.ToLowerInvariant(), "[^a-z0-9]+", " ").Trim();
}
