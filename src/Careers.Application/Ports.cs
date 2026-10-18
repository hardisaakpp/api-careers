using Careers.Domain.Applications;
using Careers.Domain.Cv;
using Careers.Domain.Offers;

namespace Careers.Application;

public interface ICvRepository
{
    /// <summary>The person's CV in <paramref name="language"/> (<see cref="CvLanguages"/>): each language is its own document.</summary>
    Task<CvDocument?> GetAsync(Guid userId, string language, CancellationToken ct);

    Task SaveAsync(Guid userId, string language, CvDocument cv, CancellationToken ct);
}

public interface IPreferencesRepository
{
    Task<SearchPreferences?> GetAsync(Guid userId, CancellationToken ct);

    Task SaveAsync(Guid userId, SearchPreferences preferences, CancellationToken ct);
}

public interface IOfferRepository
{
    /// <summary>
    /// Stores what is new. A vacancy already stored (same source and id) is left alone; one already published by another
    /// source only gains that source in <see cref="JobOffer.AlsoOn"/>. Returns how many offers were added.
    /// </summary>
    Task<int> AddNewAsync(IReadOnlyList<JobOffer> offers, CancellationToken ct);

    Task<IReadOnlyList<JobOffer>> RecentAsync(int max, CancellationToken ct);

    Task<JobOffer?> GetAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, JobOffer>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
}

public interface IApplicationRepository
{
    Task AddAsync(JobApplication application, CancellationToken ct);

    Task<JobApplication?> GetAsync(Guid userId, Guid id, CancellationToken ct);

    Task<JobApplication?> FindForOfferAsync(Guid userId, Guid offerId, CancellationToken ct);

    Task<IReadOnlyList<JobApplication>> ListAsync(Guid userId, CancellationToken ct);

    Task SaveAsync(CancellationToken ct);
}

public interface IFeedPollStore
{
    Task<DateTimeOffset?> LastAsync(string source, CancellationToken ct);

    Task MarkAsync(string source, DateTimeOffset at, CancellationToken ct);
}

/// <summary>A public source of offers whose terms allow consuming it.</summary>
public interface IOfferFeed
{
    string Source { get; }

    Task<IReadOnlyList<JobOffer>> FetchAsync(DateTimeOffset now, CancellationToken ct);
}

public sealed record RankedOffer(Guid OfferId, double Score, string? ExcludedReason, IReadOnlyList<string> Matches, int? RequiredYears = null);

/// <param name="Pending">Offers whose affinity is still being computed; ask again later.</param>
public sealed record RankResult(int Pending, IReadOnlyList<RankedOffer> Offers);

/// <summary>The stateless AI service: it receives everything it needs and keeps nothing of the person's.</summary>
public interface ICareersAi
{
    Task<RankResult> RankAsync(CvDocument cv, SearchPreferences preferences, IReadOnlyList<JobOffer> offers, CancellationToken ct);

    Task<CvDocument> AdaptAsync(CvDocument cv, JobOffer offer, CancellationToken ct);

    Task<string> LetterAsync(CvDocument cv, JobOffer offer, CancellationToken ct);

    /// <summary>The PDF of a CV, with the section titles and "present" in <paramref name="language"/> (<see cref="CvLanguages"/>).</summary>
    Task<byte[]> RenderPdfAsync(CvDocument cv, string language, CancellationToken ct);

    /// <summary>An English draft of a Spanish CV. Nothing is stored: the person reviews and edits it first.</summary>
    Task<CvTranslation> TranslateAsync(CvDocument cv, CancellationToken ct);

    /// <summary>Reads an existing CV (PDF, DOCX or a LinkedIn data export) into a draft. Nothing is stored.</summary>
    Task<CvImport> ImportAsync(byte[] file, CancellationToken ct);
}

/// <summary>A translated draft: <see cref="Warnings"/> are stable codes the interface words (e.g. no-traducido:2).</summary>
public sealed record CvTranslation(CvDocument Cv, IReadOnlyList<string> Warnings);

/// <summary>A draft read from a file: <see cref="Source"/> is pdf, docx or linkedin; <see cref="Warnings"/> are stable codes the interface words.</summary>
public sealed record CvImport(CvDocument Cv, string Source, IReadOnlyList<string> Warnings);
