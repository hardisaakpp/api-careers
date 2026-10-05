using System.Text.RegularExpressions;
using Careers.Domain.Cv;

namespace Careers.Application.Cv;

/// <summary>Limits that keep a CV, and everything built from it, a sane size. Partly filled CVs are fine: only the name is required.</summary>
public static partial class CvValidator
{
    public static void EnsureValid(CvDocument cv)
    {
        var errors = new Dictionary<string, List<string>>();
        void Add(string field, string message)
        {
            if (!errors.TryGetValue(field, out var list))
            {
                errors[field] = list = [];
            }

            list.Add(message);
        }

        void Text(string field, string? value, int max, bool required = false)
        {
            if (required && string.IsNullOrWhiteSpace(value))
            {
                Add(field, "required");
            }
            else if (value is { Length: > 0 } && value.Length > max)
            {
                Add(field, $"max-{max}");
            }
        }

        void Count(string field, int count, int max)
        {
            if (count > max)
            {
                Add(field, $"max-{max}-items");
            }
        }

        void Url(string field, string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            if (value.Length > 500 || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https"))
            {
                Add(field, "invalid-url");
            }
        }

        void Tags(string field, IReadOnlyList<string> values, int max)
        {
            Count(field, values.Count, max);
            if (values.Any(v => v.Length > 60))
            {
                Add(field, "max-60");
            }
        }

        Text("fullName", cv.FullName, 120, required: true);
        Text("headline", cv.Headline, 160);
        Text("summary", cv.Summary, 2000);
        Text("contact.email", cv.Contact.Email, 254);
        if (cv.Contact.Email.Length > 0 && !EmailPattern().IsMatch(cv.Contact.Email))
        {
            Add("contact.email", "invalid-email");
        }

        Text("contact.phone", cv.Contact.Phone, 40);
        Text("contact.location", cv.Contact.Location, 120);
        Count("contact.links", cv.Contact.Links.Count, 10);
        foreach (var (label, url) in cv.Contact.Links)
        {
            Text("contact.links", label, 40);
            Url("contact.links", url);
        }

        Count("experience", cv.Experience.Count, 30);
        for (var i = 0; i < cv.Experience.Count; i++)
        {
            var e = cv.Experience[i];
            Text($"experience[{i}].role", e.Role, 150);
            Text($"experience[{i}].organization", e.Organization, 150);
            Text($"experience[{i}].from", e.From, 20);
            Text($"experience[{i}].to", e.To, 20);
            Text($"experience[{i}].location", e.Location, 120);
            Count($"experience[{i}].achievements", e.Achievements.Count, 20);
            if (e.Achievements.Any(a => a.Length > 500))
            {
                Add($"experience[{i}].achievements", "max-500");
            }

            Tags($"experience[{i}].technologies", e.Technologies, 30);
        }

        Count("projects", cv.Projects.Count, 30);
        for (var i = 0; i < cv.Projects.Count; i++)
        {
            var p = cv.Projects[i];
            Text($"projects[{i}].name", p.Name, 150);
            Text($"projects[{i}].description", p.Description, 1000);
            Url($"projects[{i}].url", p.Url);
            Tags($"projects[{i}].technologies", p.Technologies, 30);
        }

        Count("education", cv.Education.Count, 20);
        for (var i = 0; i < cv.Education.Count; i++)
        {
            var d = cv.Education[i];
            Text($"education[{i}].title", d.Title, 150);
            Text($"education[{i}].institution", d.Institution, 150);
            Text($"education[{i}].from", d.From, 20);
            Text($"education[{i}].to", d.To, 20);
            Text($"education[{i}].location", d.Location, 120);
        }

        Count("research", cv.Research.Count, 20);
        for (var i = 0; i < cv.Research.Count; i++)
        {
            var r = cv.Research[i];
            Text($"research[{i}].role", r.Role, 150);
            Text($"research[{i}].organization", r.Organization, 150);
            Text($"research[{i}].from", r.From, 20);
            Text($"research[{i}].to", r.To, 20);
            Text($"research[{i}].location", r.Location, 120);
            Count($"research[{i}].achievements", r.Achievements.Count, 20);
            if (r.Achievements.Any(a => a.Length > 500))
            {
                Add($"research[{i}].achievements", "max-500");
            }

            Tags($"research[{i}].technologies", r.Technologies, 30);
        }

        Count("certifications", cv.Certifications.Count, 40);
        for (var i = 0; i < cv.Certifications.Count; i++)
        {
            var c = cv.Certifications[i];
            Text($"certifications[{i}].name", c.Name, 150);
            Text($"certifications[{i}].issuer", c.Issuer, 100);
            Text($"certifications[{i}].year", c.Year, 20);
        }

        Count("languages", cv.Languages.Count, 15);
        for (var i = 0; i < cv.Languages.Count; i++)
        {
            Text($"languages[{i}].name", cv.Languages[i].Name, 60);
            Text($"languages[{i}].level", cv.Languages[i].Level, 40);
        }

        Count("references", cv.References.Count, 10);
        for (var i = 0; i < cv.References.Count; i++)
        {
            var r = cv.References[i];
            Text($"references[{i}].name", r.Name, 120);
            Text($"references[{i}].role", r.Role, 150);
            Text($"references[{i}].contact", r.Contact, 120);
        }

        if (cv.Hidden.Any(key => !CvDocument.HiddenKeys.Contains(key)))
        {
            Add("hidden", "unknown-key");
        }

        Count("skills", cv.Skills.Count, 20);
        for (var i = 0; i < cv.Skills.Count; i++)
        {
            Text($"skills[{i}].name", cv.Skills[i].Name, 80);
            Tags($"skills[{i}].items", cv.Skills[i].Items, 50);
        }

        if (errors.Count > 0)
        {
            throw new ValidationFailedException(errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()));
        }
    }

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$")]
    private static partial Regex EmailPattern();
}
