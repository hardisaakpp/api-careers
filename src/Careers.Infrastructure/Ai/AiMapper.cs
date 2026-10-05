using Careers.Domain.Cv;
using Careers.Domain.Offers;

namespace Careers.Infrastructure.Ai;

internal static class AiMapper
{
    public static AiCv ToAi(CvDocument cv) => new(
        cv.FullName, cv.Headline, cv.Summary,
        new AiContact(cv.Contact.Email, cv.Contact.Phone, cv.Contact.Location, cv.Contact.Links),
        cv.Experience.Select(ToAi).ToList(),
        cv.Projects.Select(p => new AiProject(p.Name, p.Description, p.Url, p.Technologies, p.Include)).ToList(),
        cv.Education.Select(d => new AiEducation(d.Title, d.Institution, d.From, d.To, d.Location, d.Include)).ToList(),
        cv.Skills.Select(s => new AiSkillGroup(s.Name, s.Items)).ToList(),
        cv.Research.Select(ToAi).ToList(),
        cv.Certifications.Select(c => new AiCertification(c.Name, c.Issuer, c.Year, c.Include)).ToList(),
        cv.Languages.Select(l => new AiLanguage(l.Name, l.Level, l.Include)).ToList(),
        cv.Hidden);

    private static AiExperience ToAi(CvExperience e) =>
        new(e.Role, e.Organization, e.From, e.To, e.Achievements, e.Technologies, e.Location, e.Include);

    private static CvExperience FromAi(AiExperience e) =>
        new(e.Role, e.Organization, e.From, e.Achievements, e.Technologies, e.To, e.Location, e.Include);

    public static CvDocument FromAi(AiCv cv) => new(
        cv.FullName, cv.Headline, cv.Summary,
        new CvContact(cv.Contact.Email, cv.Contact.Links, cv.Contact.Phone, cv.Contact.Location),
        cv.Experience.Select(FromAi).ToList(),
        cv.Projects.Select(p => new CvProject(p.Name, p.Description, p.Technologies, p.Url, p.Include)).ToList(),
        cv.Education.Select(d => new CvEducation(d.Title, d.Institution, d.From, d.To, d.Location, d.Include)).ToList(),
        cv.Skills.Select(s => new CvSkillGroup(s.Name, s.Items)).ToList(),
        cv.Research?.Select(FromAi).ToList(),
        cv.Certifications?.Select(c => new CvCertification(c.Name, c.Issuer, c.Year, c.Include)).ToList(),
        cv.Languages?.Select(l => new CvLanguage(l.Name, l.Level, l.Include)).ToList(),
        cv.Hidden);

    public static AiPreferences ToAi(SearchPreferences p) =>
        new(p.ExcludeTitles, p.ExcludeText, p.Locations, p.RequiredTechnologies);

    /// <summary>The AI service caps each field; trimming here means an unusually long offer is shortened, not rejected.</summary>
    public static AiOffer ToAi(JobOffer o) => new(
        o.Id.ToString(), Clip(o.Title, 300), Clip(o.Company, 300), Clip(o.Location, 500),
        o.Tags.Take(40).ToList(), Clip(o.Description, 12000));

    private static string Clip(string value, int max) => value.Length <= max ? value : value[..max];
}
