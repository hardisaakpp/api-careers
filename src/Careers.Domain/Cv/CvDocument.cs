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
    IReadOnlyList<CvSkillGroup> Skills,
    IReadOnlyList<CvExperience>? Research = null,
    IReadOnlyList<CvCertification>? Certifications = null,
    IReadOnlyList<CvLanguage>? Languages = null,
    IReadOnlyList<string>? Hidden = null)
{
    // Older stored CVs lack these: they read back as empty, never null.
    public IReadOnlyList<CvExperience> Research { get; init; } = Research ?? [];

    public IReadOnlyList<CvCertification> Certifications { get; init; } = Certifications ?? [];

    public IReadOnlyList<CvLanguage> Languages { get; init; } = Languages ?? [];

    /// <summary>What stays out of the PDF: section keys (<see cref="HiddenKeys"/>). Everything is stored either way; empty = the recommended default.</summary>
    public IReadOnlyList<string> Hidden { get; init; } = Hidden ?? [];

    /// <summary>The sections and contact details a person can switch off for the PDF.</summary>
    public static IReadOnlySet<string> HiddenKeys { get; } = new HashSet<string>
    {
        "resumen", "experiencia", "investigacion", "proyectos", "educacion", "habilidades", "certificaciones", "idiomas",
        "contacto.telefono", "contacto.ubicacion", "contacto.enlaces",
    };

    public static CvDocument Empty { get; } = new(
        string.Empty, string.Empty, string.Empty, new CvContact(string.Empty, new Dictionary<string, string>()),
        [], [], [], []);

    /// <summary>Every technology the person declares anywhere: what offers are compared against.</summary>
    public IReadOnlySet<string> Technologies()
    {
        var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        all.UnionWith(Experience.Concat(Research).SelectMany(e => e.Technologies));
        all.UnionWith(Projects.SelectMany(p => p.Technologies));
        all.UnionWith(Skills.SelectMany(s => s.Items));
        return all;
    }
}

public sealed record CvContact(string Email, IReadOnlyDictionary<string, string> Links, string? Phone = null, string? Location = null);

public sealed record CvExperience(
    string Role, string Organization, string From,
    IReadOnlyList<string> Achievements, IReadOnlyList<string> Technologies, string? To = null,
    string? Location = null, bool Include = true);

public sealed record CvProject(string Name, string Description, IReadOnlyList<string> Technologies, string? Url = null, bool Include = true);

public sealed record CvEducation(string Title, string Institution, string From, string? To = null, string? Location = null, bool Include = true);

public sealed record CvCertification(string Name, string Issuer = "", string Year = "", bool Include = true);

public sealed record CvLanguage(string Name, string Level = "", bool Include = true);

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
