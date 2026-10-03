namespace Careers.Domain.Applications;

public enum ApplicationStatus
{
    Draft,
    Confirmed,
    Sent,
    Discarded,
}

public sealed class InvalidStatusTransitionException(ApplicationStatus from, ApplicationStatus to)
    : Exception($"An application that is {from} cannot become {to}.")
{
    public ApplicationStatus From { get; } = from;
    public ApplicationStatus To { get; } = to;
}

/// <summary>
/// What a person prepared for one offer. This system never sends anything: the person applies on the offer's page and
/// records it here, and nothing can be marked as sent without being confirmed first.
/// </summary>
public sealed class JobApplication
{
    public const int MaxLetterLength = 8000;

    private JobApplication() { }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid OfferId { get; private set; }
    public ApplicationStatus Status { get; private set; }
    public string CoverLetter { get; private set; } = string.Empty;
    /// <summary>The CV tailored to this offer, as JSON: the exact version the person reviewed.</summary>
    public string TailoredCvJson { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static JobApplication Prepare(Guid userId, Guid offerId, string coverLetter, string tailoredCvJson, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        OfferId = offerId,
        Status = ApplicationStatus.Draft,
        CoverLetter = coverLetter,
        TailoredCvJson = tailoredCvJson,
        CreatedAt = now,
        UpdatedAt = now,
    };

    public void EditLetter(string letter, DateTimeOffset now)
    {
        if (letter.Length > MaxLetterLength)
        {
            throw new ArgumentException($"The letter cannot exceed {MaxLetterLength} characters.", nameof(letter));
        }

        if (Status is ApplicationStatus.Sent or ApplicationStatus.Discarded)
        {
            throw new InvalidStatusTransitionException(Status, Status);
        }

        CoverLetter = letter;
        UpdatedAt = now;
    }

    public void Confirm(DateTimeOffset now) => MoveTo(ApplicationStatus.Confirmed, now);

    public void MarkSent(DateTimeOffset now) => MoveTo(ApplicationStatus.Sent, now);

    public void Discard(DateTimeOffset now) => MoveTo(ApplicationStatus.Discarded, now);

    private void MoveTo(ApplicationStatus next, DateTimeOffset now)
    {
        var allowed = Status switch
        {
            ApplicationStatus.Draft => next is ApplicationStatus.Confirmed or ApplicationStatus.Discarded,
            ApplicationStatus.Confirmed => next is ApplicationStatus.Sent or ApplicationStatus.Discarded,
            _ => false,
        };
        if (!allowed)
        {
            throw new InvalidStatusTransitionException(Status, next);
        }

        Status = next;
        UpdatedAt = now;
    }
}
