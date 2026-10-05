using Careers.Domain.Cv;

namespace Careers.Application.Cv;

public sealed class CvService(ICvRepository cvs, IPreferencesRepository preferences, ICareersAi ai)
{
    public async Task<(CvDocument Cv, bool Saved)> GetAsync(Guid userId, CancellationToken ct)
    {
        var cv = await cvs.GetAsync(userId, ct);
        return (cv ?? CvDocument.Empty, cv is not null);
    }

    public async Task SaveAsync(Guid userId, CvDocument cv, CancellationToken ct)
    {
        CvValidator.EnsureValid(cv);
        await cvs.SaveAsync(userId, cv, ct);
    }

    /// <summary>The PDF of the draft being edited, or of the saved CV when there is no draft. Nothing is stored.</summary>
    public async Task<byte[]> RenderAsync(Guid userId, CvDocument? draft, CancellationToken ct)
    {
        var cv = draft ?? await cvs.GetAsync(userId, ct) ?? throw new ConflictException("cv-missing", "Save your CV first.");
        CvValidator.EnsureValid(cv);
        return await ai.RenderPdfAsync(cv, ct);
    }

    /// <summary>Reads an existing CV file into a draft for the form. Nothing is stored: the person reviews it and saves.</summary>
    public async Task<CvImport> ImportAsync(byte[] file, CancellationToken ct)
    {
        var imported = await ai.ImportAsync(file, ct);
        return imported with { Cv = CvDraftCleaner.Clean(imported.Cv) };
    }

    public async Task<SearchPreferences> GetPreferencesAsync(Guid userId, CancellationToken ct) =>
        await preferences.GetAsync(userId, ct) ?? SearchPreferences.None;

    public async Task SavePreferencesAsync(Guid userId, SearchPreferences value, CancellationToken ct)
    {
        var lists = new Dictionary<string, IReadOnlyList<string>>
        {
            ["excludeTitles"] = value.ExcludeTitles,
            ["excludeText"] = value.ExcludeText,
            ["locations"] = value.Locations,
            ["requiredTechnologies"] = value.RequiredTechnologies,
        };
        var errors = lists
            .Where(l => l.Value.Count > 30 || l.Value.Any(v => v.Length > 80))
            .ToDictionary(l => l.Key, _ => new[] { "too-long" });
        if (errors.Count > 0)
        {
            throw new ValidationFailedException(errors);
        }

        SearchPreferences Clean(SearchPreferences p) => new(Trim(p.ExcludeTitles), Trim(p.ExcludeText), Trim(p.Locations), Trim(p.RequiredTechnologies));
        await preferences.SaveAsync(userId, Clean(value), ct);
    }

    private static List<string> Trim(IReadOnlyList<string> values) =>
        values.Select(v => v.Trim()).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
