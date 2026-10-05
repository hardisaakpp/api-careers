using System.Text.RegularExpressions;
using Careers.Domain.Cv;

namespace Careers.Application.Cv;

/// <summary>
/// Makes an imported draft fit the same limits <see cref="CvValidator"/> enforces on save, so that what the person reviews
/// can always be saved: long text is cut, extra items dropped, and a link that is not a full http(s) address is left out.
/// The limits mirror the validator's; a test keeps them in step.
/// </summary>
public static partial class CvDraftCleaner
{
    public static CvDocument Clean(CvDocument cv) => new(
        Cut(cv.FullName, 120), Cut(cv.Headline, 160), Cut(cv.Summary, 2000),
        new CvContact(Email(cv.Contact.Email), Links(cv.Contact.Links), CutOrNull(cv.Contact.Phone, 40), CutOrNull(cv.Contact.Location, 120)),
        cv.Experience.Take(30).Select(Work).ToList(),
        cv.Projects.Take(30).Select(p => new CvProject(Cut(p.Name, 150), Cut(p.Description, 1000), Tags(p.Technologies, 30), Url(p.Url), p.Include)).ToList(),
        cv.Education.Take(20).Select(d => new CvEducation(Cut(d.Title, 150), Cut(d.Institution, 150), Cut(d.From, 20), CutOrNull(d.To, 20), CutOrNull(d.Location, 120), d.Include)).ToList(),
        cv.Skills.Take(20).Select(s => new CvSkillGroup(Cut(s.Name, 80), Tags(s.Items, 50))).ToList(),
        cv.Research.Take(20).Select(Work).ToList(),
        cv.Certifications.Take(40).Select(c => new CvCertification(Cut(c.Name, 150), Cut(c.Issuer, 100), Cut(c.Year, 20), c.Include)).ToList(),
        cv.Languages.Take(15).Select(l => new CvLanguage(Cut(l.Name, 60), Cut(l.Level, 40), l.Include)).ToList(),
        cv.Hidden.Where(CvDocument.HiddenKeys.Contains).Distinct().ToList(),
        cv.References.Take(10).Select(r => new CvReference(Cut(r.Name, 120), Cut(r.Role, 150), Cut(r.Contact, 120), r.Include)).ToList());

    private static CvExperience Work(CvExperience e) => new(
        Cut(e.Role, 150), Cut(e.Organization, 150), Cut(e.From, 20),
        e.Achievements.Take(20).Select(a => Cut(a, 500)).ToList(), Tags(e.Technologies, 30),
        CutOrNull(e.To, 20), CutOrNull(e.Location, 120), e.Include);

    private static string Cut(string? value, int max) => value is null ? string.Empty : value.Length <= max ? value : value[..max];

    private static string? CutOrNull(string? value, int max) => string.IsNullOrEmpty(value) ? null : Cut(value, max);

    private static List<string> Tags(IReadOnlyList<string> values, int count) =>
        values.Take(count).Select(v => Cut(v, 60)).ToList();

    private static string? Url(string? value) =>
        !string.IsNullOrEmpty(value) && value.Length <= 500
        && Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? value : null;

    private static string Email(string value) => EmailPattern().IsMatch(value) && value.Length <= 254 ? value : string.Empty;

    private static Dictionary<string, string> Links(IReadOnlyDictionary<string, string> links) =>
        links.Where(l => Url(l.Value) is not null).Take(10).ToDictionary(l => Cut(l.Key, 40), l => l.Value);

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$")]
    private static partial Regex EmailPattern();
}
