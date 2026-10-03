using System.Text.Json;
using Careers.Domain.Applications;
using Careers.Domain.Cv;
using Careers.Domain.Offers;

namespace Careers.Application.Applications;

public sealed record ApplicationDetail(JobApplication Application, JobOffer Offer);

public enum ApplicationAction
{
    Confirm,
    MarkSent,
    Discard,
}

public sealed class ApplicationService(
    ICvRepository cvs,
    IOfferRepository offers,
    IApplicationRepository applications,
    ICareersAi ai,
    TimeProvider clock)
{
    /// <summary>
    /// Prepares the draft for an offer: the CV tailored to it and a cover letter. Calling it again returns the same draft.
    /// Nothing is stored unless the AI service answered.
    /// </summary>
    public async Task<(Guid Id, bool Created)> PrepareAsync(Guid userId, Guid offerId, CancellationToken ct)
    {
        var offer = await offers.GetAsync(offerId, ct) ?? throw new NotFoundException("Offer not found.");
        var existing = await applications.FindForOfferAsync(userId, offerId, ct);
        if (existing is not null)
        {
            return (existing.Id, false);
        }

        var cv = await cvs.GetAsync(userId, ct) ?? throw new ConflictException("cv-missing", "Save your CV first.");
        var tailored = await ai.AdaptAsync(cv, offer, ct);
        var letter = await ai.LetterAsync(tailored, offer, ct);
        var application = JobApplication.Prepare(
            userId, offerId, letter, JsonSerializer.Serialize(tailored, CareersJson.Options), clock.GetUtcNow());
        await applications.AddAsync(application, ct);
        return (application.Id, true);
    }

    public async Task<IReadOnlyList<ApplicationDetail>> ListAsync(Guid userId, CancellationToken ct)
    {
        var mine = await applications.ListAsync(userId, ct);
        var found = await offers.GetManyAsync(mine.Select(a => a.OfferId).ToList(), ct);
        return mine.Where(a => found.ContainsKey(a.OfferId)).Select(a => new ApplicationDetail(a, found[a.OfferId])).ToList();
    }

    public async Task<ApplicationDetail> GetAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var application = await Own(userId, id, ct);
        var offer = await offers.GetAsync(application.OfferId, ct) ?? throw new NotFoundException();
        return new ApplicationDetail(application, offer);
    }

    public async Task EditLetterAsync(Guid userId, Guid id, string letter, CancellationToken ct)
    {
        var application = await Own(userId, id, ct);
        try
        {
            application.EditLetter(letter, clock.GetUtcNow());
        }
        catch (ArgumentException)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]> { ["coverLetter"] = [$"max-{JobApplication.MaxLetterLength}"] });
        }
        catch (InvalidStatusTransitionException e)
        {
            throw new ConflictException("invalid-transition", e.Message);
        }

        await applications.SaveAsync(ct);
    }

    public async Task<ApplicationStatus> ActAsync(Guid userId, Guid id, ApplicationAction action, CancellationToken ct)
    {
        var application = await Own(userId, id, ct);
        try
        {
            var now = clock.GetUtcNow();
            switch (action)
            {
                case ApplicationAction.Confirm:
                    application.Confirm(now);
                    break;
                case ApplicationAction.MarkSent:
                    application.MarkSent(now);
                    break;
                default:
                    application.Discard(now);
                    break;
            }
        }
        catch (InvalidStatusTransitionException e)
        {
            throw new ConflictException("invalid-transition", e.Message);
        }

        await applications.SaveAsync(ct);
        return application.Status;
    }

    /// <summary>The PDF of exactly the CV the person reviewed for this offer.</summary>
    public async Task<byte[]> RenderPdfAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var application = await Own(userId, id, ct);
        var cv = JsonSerializer.Deserialize<CvDocument>(application.TailoredCvJson, CareersJson.Options)
                 ?? throw new NotFoundException();
        return await ai.RenderPdfAsync(cv, ct);
    }

    /// <summary>Someone else's application is indistinguishable from one that does not exist.</summary>
    private async Task<JobApplication> Own(Guid userId, Guid id, CancellationToken ct) =>
        await applications.GetAsync(userId, id, ct) ?? throw new NotFoundException();
}
