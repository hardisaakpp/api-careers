namespace Careers.Infrastructure.Feeds;

internal static class FeedHttp
{
    public const int MaxBytes = 8_000_000;

    /// <summary>Only https, and never more than <see cref="MaxBytes"/>: a feed is third-party input.</summary>
    public static async Task<byte[]> GetAsync(HttpClient http, string url, CancellationToken ct)
    {
        if (!url.StartsWith("https://", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Feeds are only read over https.");
        }

        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxBytes)
        {
            throw new InvalidOperationException("The feed is too large.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxBytes)
            {
                throw new InvalidOperationException("The feed is too large.");
            }
        }

        return buffer.ToArray();
    }

    public static string? SafeUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && url!.Length <= 1000 ? url : null;

    public static string Clip(string? value, int max) =>
        value is null ? string.Empty : value.Length <= max ? value : value[..max];
}
