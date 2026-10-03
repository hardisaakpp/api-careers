using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Careers.Application;
using Careers.Domain.Cv;
using Careers.Infrastructure.Persistence;
using Careers.UnitTests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Careers.UnitTests.Api;

public sealed class CareersApiFactory : WebApplicationFactory<Program>
{
    private const string JwtKey = "test-signing-key-test-signing-key-0123456789";
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"careers-test-{Guid.NewGuid():N}.db");

    public FakeAi Ai { get; } = new();
    public TestClock Clock { get; } = new();
    public FakeFeed Alpha { get; } = new("alpha");
    public FakeFeed Beta { get; } = new("beta");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Jwt:Key", JwtKey);
        builder.UseSetting("Jwt:Issuer", "stockline");
        builder.UseSetting("Jwt:Audience", "stockline-api");
        builder.UseSetting("ConnectionStrings:Default", "unused");
        builder.UseSetting("OfferFeeds:AutoRefresh", "false");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<CareersDbContext>>();
            foreach (var descriptor in services
                         .Where(d => d.ServiceType.IsGenericType
                                     && d.ServiceType.Name.StartsWith("IDbContextOptionsConfiguration")
                                     && d.ServiceType.GenericTypeArguments[0] == typeof(CareersDbContext))
                         .ToList())
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<CareersDbContext>(options => options.UseSqlite($"Data Source={_dbPath}"));
            services.RemoveAll<ICareersAi>();
            services.AddSingleton<ICareersAi>(Ai);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.RemoveAll<IOfferFeed>();
            services.AddSingleton<IOfferFeed>(Alpha);
            services.AddSingleton<IOfferFeed>(Beta);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CareersDbContext>();
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
        return host;
    }

    /// <summary>A client signed in as a person, with the kind of token api-stockline issues.</summary>
    public HttpClient ClientFor(Guid? userId = null)
    {
        var client = CreateClient();
        if (userId is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(userId.Value));
        }

        return client;
    }

    public static string Token(Guid userId, string issuer = "stockline", string key = JwtKey) =>
        Token(userId.ToString(), issuer, key);

    public static string Token(string subject, string issuer = "stockline", string key = JwtKey) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = "stockline-api",
            Subject = new ClaimsIdentity([new Claim("sub", subject), new Claim("role", "Buyer"), new Claim("is_admin", "false")]),
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256),
        });

    public static CvDocument SampleCv(string name = "Ana Example") => new(
        name, "Backend engineer", "APIs in .NET.",
        new CvContact("ana@example.com", new Dictionary<string, string> { ["GitHub"] = "https://github.com/ana" }, "+593 99", "Quito"),
        [new CvExperience("Developer", "Acme", "2022-03", ["Cut a query from 8 s to 300 ms"], [".NET", "SQL Server"])],
        [new CvProject("Stockline", "Orders system", ["Next.js"], "https://example.com")],
        [new CvEducation("Systems Engineering", "Demo University", "2016", "2021")],
        [new CvSkillGroup("Languages", ["C#", "TypeScript"])]);

    public static async Task<JsonElement> Json(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>())!;

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            using (var own = new SqliteConnection($"Data Source={_dbPath}"))
            {
                SqliteConnection.ClearPool(own);
            }

            foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
            {
                File.Delete(_dbPath + suffix);
            }
        }
    }
}
