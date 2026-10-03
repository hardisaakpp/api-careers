namespace Careers.Domain.Cv;

/// <summary>A person's CV as data: every version (PDF, tailored to an offer) is a view of this, never an edit of it.</summary>
public sealed record CvDocument(
    string FullName,
    string Headline,
    string Summary,
    CvContact Contact,
    IReadOnlyList<CvExperience> Experience,
    IReadOnlyList<CvProject> Projects,
    IReadOnlyList<CvEducation> Education,
    IReadOnlyList<CvSkillGroup> Skills)
{
    public static CvDocument Empty { get; } = new(
        string.Empty, string.Empty, string.Empty, new CvContact(string.Empty, new Dictionary<string, string>()),
        [], [], [], []);

    /// <summary>Every technology the person declares anywhere: what offers are compared against.</summary>
    public IReadOnlySet<string> Technologies()
    {
        var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        all.UnionWith(Experience.SelectMany(e => e.Technologies));
        all.UnionWith(Projects.SelectMany(p => p.Technologies));
        all.UnionWith(Skills.SelectMany(s => s.Items));
        return all;
    }
}

public sealed record CvContact(string Email, IReadOnlyDictionary<string, string> Links, string? Phone = null, string? Location = null);

public sealed record CvExperience(
    string Role, string Organization, string From,
    IReadOnlyList<string> Achievements, IReadOnlyList<string> Technologies, string? To = null);

public sealed record CvProject(string Name, string Description, IReadOnlyList<string> Technologies, string? Url = null);

public sealed record CvEducation(string Title, string Institution, string From, string? To = null);

public sealed record CvSkillGroup(string Name, IReadOnlyList<string> Items);

/// <summary>Hard rules: what drops an offer no matter how close it is.</summary>
public sealed record SearchPreferences(
    IReadOnlyList<string> ExcludeTitles,
    IReadOnlyList<string> ExcludeText,
    IReadOnlyList<string> Locations,
    IReadOnlyList<string> RequiredTechnologies)
{
    public static SearchPreferences None { get; } = new([], [], [], []);
}
