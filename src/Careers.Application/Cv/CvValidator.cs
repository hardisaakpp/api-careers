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
