namespace Careers.Domain.Cv;

/// <summary>The languages a CV can be kept and rendered in. Spanish is the original; English is a version of it that the person edits.</summary>
public static class CvLanguages
{
    public const string Spanish = "es";
    public const string English = "en";

    public static bool IsSupported(string? language) => language is Spanish or English;
}
