using System.Text.Json;

namespace Careers.Infrastructure.Feeds;

internal static class FeedJson
{
    /// <summary>A string property, or null when it is missing or not a string.</summary>
    public static string? Text(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    /// <summary>An id that may come as a number or as a string.</summary>
    public static string? Id(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.String or JsonValueKind.Number
            ? v.ToString()
            : null;

    public static IEnumerable<string> Strings(JsonElement e, string name, int max = 40) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => FeedHttp.Clip(x.GetString(), 60)).Take(max)
            : [];

    public static DateTimeOffset? Date(string? text) =>
        DateTimeOffset.TryParse(text, null, System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d : null;

    public static DateTimeOffset? Unix(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var s)
            ? DateTimeOffset.FromUnixTimeSeconds(s)
            : null;
}
