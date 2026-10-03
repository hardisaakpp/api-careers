using System.Text.Json;
using Careers.Application;
using Careers.Domain.Applications;
using Careers.Domain.Cv;
using Careers.Domain.Offers;
using Microsoft.EntityFrameworkCore;

namespace Careers.Infrastructure.Persistence;

public sealed class EfCvRepository(CareersDbContext db, TimeProvider clock) : ICvRepository
{
    public async Task<CvDocument?> GetAsync(Guid userId, CancellationToken ct)
    {
        var row = await db.Cvs.AsNoTracking().SingleOrDefaultAsync(r => r.UserId == userId, ct);
        return row is null ? null : JsonSerializer.Deserialize<CvDocument>(row.Json, CareersJson.Options);
    }

    public async Task SaveAsync(Guid userId, CvDocument cv, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(cv, CareersJson.Options);
        var row = await db.Cvs.SingleOrDefaultAsync(r => r.UserId == userId, ct);
        if (row is null)
        {
            db.Cvs.Add(new CvRow { UserId = userId, Json = json, UpdatedAt = clock.GetUtcNow() });
        }
        else
        {
            row.Json = json;
            row.UpdatedAt = clock.GetUtcNow();
        }

        await db.SaveChangesAsync(ct);
    }
}

public sealed class EfPreferencesRepository(CareersDbContext db, TimeProvider clock) : IPreferencesRepository
{
    public async Task<SearchPreferences?> GetAsync(Guid userId, CancellationToken ct)
    {
        var row = await db.Preferences.AsNoTracking().SingleOrDefaultAsync(r => r.UserId == userId, ct);
        return row is null ? null : JsonSerializer.Deserialize<SearchPreferences>(row.Json, CareersJson.Options);
    }

    public async Task SaveAsync(Guid userId, SearchPreferences preferences, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(preferences, CareersJson.Options);
        var row = await db.Preferences.SingleOrDefaultAsync(r => r.UserId == userId, ct);
        if (row is null)
        {
            db.Preferences.Add(new PreferencesRow { UserId = userId, Json = json, UpdatedAt = clock.GetUtcNow() });
        }
        else
        {
            row.Json = json;
            row.UpdatedAt = clock.GetUtcNow();
        }

        await db.SaveChangesAsync(ct);
    }
}

public sealed class EfOfferRepository(CareersDbContext db) : IOfferRepository
{
    public async Task<int> AddNewAsync(IReadOnlyList<JobOffer> offers, CancellationToken ct)
    {
        var keys = offers.Select(o => o.DedupKey).Distinct().ToList();
        var byKey = new Dictionary<string, JobOffer>();
        foreach (var chunk in keys.Chunk(500))
        {
            foreach (var found in await db.Offers.Where(o => chunk.Contains(o.DedupKey)).ToListAsync(ct))
            {
                byKey[found.DedupKey] = found;
            }
        }

        // A vacancy whose title was edited keeps its (source, id) but changes key: it is the same one, not a new one.
        var known = new HashSet<(string, string)>();
        foreach (var group in offers.GroupBy(o => o.Source))
        {
            var ids = group.Select(o => o.SourceId).Distinct().ToList();
            foreach (var chunk in ids.Chunk(500))
            {
                var found = await db.Offers.Where(o => o.Source == group.Key && chunk.Contains(o.SourceId))
                    .Select(o => o.SourceId).ToListAsync(ct);
                known.UnionWith(found.Select(id => (group.Key, id)));
            }
        }

        var added = 0;
        foreach (var offer in offers)
        {
            if (known.Contains((offer.Source, offer.SourceId)))
            {
                continue;
            }

            if (!byKey.TryGetValue(offer.DedupKey, out var existing))
            {
                db.Offers.Add(offer);
                byKey[offer.DedupKey] = offer;
                known.Add((offer.Source, offer.SourceId));
                added++;
            }
            else if (existing.Source != offer.Source)
            {
                existing.AlsoPublishedOn(offer.Source);
            }
        }

        await db.SaveChangesAsync(ct);
        return added;
    }

    public async Task<IReadOnlyList<JobOffer>> RecentAsync(int max, CancellationToken ct) =>
        await db.Offers.AsNoTracking()
            .OrderByDescending(o => o.PublishedAt ?? o.FetchedAt).ThenBy(o => o.Id)
            .Take(max).ToListAsync(ct);

    public Task<JobOffer?> GetAsync(Guid id, CancellationToken ct) =>
        db.Offers.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, ct);

    public async Task<IReadOnlyDictionary<Guid, JobOffer>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        await db.Offers.AsNoTracking().Where(o => ids.Contains(o.Id)).ToDictionaryAsync(o => o.Id, ct);
}

public sealed class EfApplicationRepository(CareersDbContext db) : IApplicationRepository
{
    public async Task AddAsync(JobApplication application, CancellationToken ct)
    {
        db.Applications.Add(application);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (UniqueViolations.IsUnique(e))
        {
            // Two clicks at once: the second one finds the draft the first created.
            db.Entry(application).State = EntityState.Detached;
            throw new ConflictException("already-prepared", "This offer already has a draft.");
        }
    }

    public Task<JobApplication?> GetAsync(Guid userId, Guid id, CancellationToken ct) =>
        db.Applications.SingleOrDefaultAsync(a => a.Id == id && a.UserId == userId, ct);

    public Task<JobApplication?> FindForOfferAsync(Guid userId, Guid offerId, CancellationToken ct) =>
        db.Applications.AsNoTracking().SingleOrDefaultAsync(a => a.UserId == userId && a.OfferId == offerId, ct);

    public async Task<IReadOnlyList<JobApplication>> ListAsync(Guid userId, CancellationToken ct) =>
        await db.Applications.AsNoTracking().Where(a => a.UserId == userId)
            .OrderByDescending(a => a.UpdatedAt).ToListAsync(ct);

    public Task SaveAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}

public sealed class EfFeedPollStore(CareersDbContext db) : IFeedPollStore
{
    public async Task<DateTimeOffset?> LastAsync(string source, CancellationToken ct) =>
        (await db.FeedPolls.AsNoTracking().SingleOrDefaultAsync(p => p.Source == source, ct))?.PolledAt;

    public async Task MarkAsync(string source, DateTimeOffset at, CancellationToken ct)
    {
        var row = await db.FeedPolls.SingleOrDefaultAsync(p => p.Source == source, ct);
        if (row is null)
        {
            db.FeedPolls.Add(new FeedPollRow { Source = source, PolledAt = at });
        }
        else
        {
            row.PolledAt = at;
        }

        await db.SaveChangesAsync(ct);
    }
}

internal static class UniqueViolations
{
    /// <summary>SQL Server reports 2601/2627; SQLite says "UNIQUE constraint failed".</summary>
    public static bool IsUnique(DbUpdateException e) =>
        e.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 }
        || e.InnerException?.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) == true;
}
