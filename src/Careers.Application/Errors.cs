namespace Careers.Application;

/// <summary>An expected failure, with a stable <see cref="Code"/> the interface can translate.</summary>
public abstract class CareersException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class NotFoundException(string message = "Not found.") : CareersException("not-found", message);

public sealed class ConflictException(string code, string message) : CareersException(code, message);

public sealed class ValidationFailedException(IReadOnlyDictionary<string, string[]> errors)
    : CareersException("validation", "One or more fields are not valid.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

/// <summary>The AI service is down or answered something unusable. Nothing was saved.</summary>
public sealed class AiUnavailableException(string message, Exception? inner = null)
    : CareersException("ai-unavailable", message)
{
    public override string ToString() => inner is null ? base.ToString() : $"{base.ToString()} ---> {inner}";
}

/// <summary>The uploaded file could not be turned into a CV. The code says why (e.g. <c>import-sin-texto</c> for a scanned PDF).</summary>
public sealed class ImportFailedException(string reason)
    : CareersException($"import-{reason}", "The file could not be imported.")
{
    public bool TooLarge => Reason == "demasiado-grande";

    public string Reason { get; } = reason;
}
