using System.Text.Json;
using Careers.Domain.Applications;
using Careers.Domain.Offers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Careers.Infrastructure.Persistence;

/// <summary>A CV or a set of preferences, stored whole: they are edited as a document and never queried field by field.</summary>
public sealed class CvRow
{
    public Guid UserId { get; set; }
    public string Json { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class PreferencesRow
{
    public Guid UserId { get; set; }
    public string Json { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class FeedPollRow
{
    public string Source { get; set; } = string.Empty;
    public DateTimeOffset PolledAt { get; set; }
}

public sealed class CareersDbContext(DbContextOptions<CareersDbContext> options) : DbContext(options)
{
    public DbSet<JobOffer> Offers => Set<JobOffer>();
    public DbSet<JobApplication> Applications => Set<JobApplication>();
    public DbSet<CvRow> Cvs => Set<CvRow>();
    public DbSet<PreferencesRow> Preferences => Set<PreferencesRow>();
    public DbSet<FeedPollRow> FeedPolls => Set<FeedPollRow>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Instants are stored as UTC ticks: sortable and comparable on every provider.
        builder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetTicksConverter>();
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<CvRow>(b =>
        {
            b.ToTable("cvs");
            b.HasKey(x => x.UserId);
            b.Property(x => x.Json).IsRequired();
        });
        model.Entity<PreferencesRow>(b =>
        {
            b.ToTable("preferences");
            b.HasKey(x => x.UserId);
            b.Property(x => x.Json).IsRequired();
        });
        model.Entity<FeedPollRow>(b =>
        {
            b.ToTable("feed_polls");
            b.HasKey(x => x.Source);
            b.Property(x => x.Source).HasMaxLength(40);
        });
        model.Entity<JobOffer>(b =>
        {
            b.ToTable("offers");
            b.HasKey(x => x.Id);
            b.Property(x => x.Source).HasMaxLength(40).IsRequired();
            b.Property(x => x.SourceId).HasMaxLength(200).IsRequired();
            b.Property(x => x.Url).HasMaxLength(1000).IsRequired();
            b.Property(x => x.Title).HasMaxLength(300).IsRequired();
            b.Property(x => x.Company).HasMaxLength(300).IsRequired();
            b.Property(x => x.Location).HasMaxLength(500).IsRequired();
            b.Property(x => x.DedupKey).HasMaxLength(16).IsRequired();
            b.Property(x => x.AlsoOn).HasMaxLength(200).IsRequired();
            b.Property(x => x.Tags).HasConversion(Json<List<string>>(), ListComparer()).HasField("<Tags>k__BackingField");
            b.HasIndex(x => new { x.Source, x.SourceId }).IsUnique();
            b.HasIndex(x => x.DedupKey).IsUnique();
            b.HasIndex(x => x.PublishedAt);
        });
        model.Entity<JobApplication>(b =>
        {
            b.ToTable("applications");
            b.HasKey(x => x.Id);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.CoverLetter).HasMaxLength(JobApplication.MaxLetterLength);
            b.HasOne<JobOffer>().WithMany().HasForeignKey(x => x.OfferId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.UserId, x.OfferId }).IsUnique();
            b.HasIndex(x => x.UserId);
        });
    }

    private static Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<IReadOnlyList<string>, string> Json<T>() =>
        new(v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => (IReadOnlyList<string>)(JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>()));

    private static ValueComparer<IReadOnlyList<string>> ListComparer() => new(
        (a, b) => a != null && b != null && a.SequenceEqual(b),
        v => v.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
        v => v.ToList());
}

public sealed class DateTimeOffsetTicksConverter()
    : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTimeOffset, long>(
        v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));
