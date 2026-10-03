using Careers.Domain.Applications;
using Careers.Domain.Cv;
using Careers.Domain.Offers;

namespace Careers.Application.Offers;

public sealed record OfferListItem(
    JobOffer Offer, double Score, IReadOnlyList<string> Matches, Guid? ApplicationId, ApplicationStatus? ApplicationStatus);

/// <param name="Total">Offers known.</param>
/// <param name="Excluded">Dropped by the person's hard rules.</param>
/// <param name="Pending">Offers whose affinity is still being computed.</param>
public sealed record OfferRanking(int Total, int Excluded, int Pending, IReadOnlyList<OfferListItem> Offers);

public sealed class RankingService(
    ICvRepository cvs,
    IPreferencesRepository preferences,
    IOfferRepository offers,
    IApplicationRepository applications,
    ICareersAi ai)
{
    public const int MaxOffers = 500;

    public async Task<OfferRanking> RankAsync(Guid userId, int limit, CancellationToken ct)
    {
        var cv = await cvs.GetAsync(userId, ct) ?? throw new ConflictException("cv-missing", "Save your CV first.");
        var prefs = await preferences.GetAsync(userId, ct) ?? SearchPreferences.None;
        var known = await offers.RecentAsync(MaxOffers, ct);
        if (known.Count == 0)
        {
            return new OfferRanking(0, 0, 0, []);
        }

        var ranked = await ai.RankAsync(cv, prefs, known, ct);
        var byId = known.ToDictionary(o => o.Id);
        var mine = (await applications.ListAsync(userId, ct)).ToDictionary(a => a.OfferId);

        var shown = ranked.Offers
            .Where(r => r.ExcludedReason is null && byId.ContainsKey(r.OfferId))
            .OrderByDescending(r => r.Score)
            .Take(Math.Clamp(limit, 1, MaxOffers))
            .Select(r =>
            {
                mine.TryGetValue(r.OfferId, out var application);
                return new OfferListItem(byId[r.OfferId], r.Score, r.Matches, application?.Id, application?.Status);
            })
            .ToList();
        return new OfferRanking(known.Count, ranked.Offers.Count(r => r.ExcludedReason is not null), ranked.Pending, shown);
    }
}
