using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Careers.Domain.Cv;

namespace Careers.UnitTests.Api;

public class OfferEndpointsTests : IClassFixture<CareersApiFactory>, IAsyncLifetime
{
    private readonly CareersApiFactory _factory;

    public OfferEndpointsTests(CareersApiFactory factory) => _factory = factory;

    public Task InitializeAsync()
    {
        _factory.Alpha.Offers.Clear();
        _factory.Beta.Offers.Clear();
        _factory.Alpha.Fail = false;
        _factory.Ai.Pending = 0;
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private async Task<JsonElement> Refresh(HttpClient client, bool force = false) =>
        await CareersApiFactory.Json(await client.PostAsync($"/offers/refresh?force={force.ToString().ToLowerInvariant()}", null));

    [Fact]
    public async Task Refresh_stores_new_offers_and_respects_the_interval_per_source()
    {
        var client = _factory.ClientFor(Guid.NewGuid());
        _factory.Clock.Advance(TimeSpan.FromHours(7)); // tests in this class share clock and database
        var title = Unique("Backend engineer");
        _factory.Alpha.Offers.Add((Unique("a"), title, "Acme"));
        _factory.Beta.Offers.Add((Unique("b"), Unique("Designer"), "Foo"));

        var first = (await Refresh(client)).GetProperty("results");
        Assert.Equal(2, first.GetArrayLength());
        Assert.All(first.EnumerateArray(), r => Assert.Equal("Fetched", r.GetProperty("outcome").GetString()));

        var callsBefore = _factory.Alpha.Calls;
        var second = (await Refresh(client)).GetProperty("results");
        Assert.All(second.EnumerateArray(), r => Assert.Equal("Skipped", r.GetProperty("outcome").GetString()));
        Assert.Equal(callsBefore, _factory.Alpha.Calls);

        _factory.Clock.Advance(TimeSpan.FromHours(7));
        var third = (await Refresh(client)).GetProperty("results");
        Assert.All(third.EnumerateArray(), r => Assert.Equal("Fetched", r.GetProperty("outcome").GetString()));
        Assert.All(third.EnumerateArray(), r => Assert.Equal(0, r.GetProperty("added").GetInt32())); // nothing new the second time
    }

    [Fact]
    public async Task Forcing_ignores_the_interval()
    {
        var client = _factory.ClientFor(Guid.NewGuid());
        await Refresh(client);
        var calls = _factory.Alpha.Calls;

        await Refresh(client, force: true);

        Assert.Equal(calls + 1, _factory.Alpha.Calls);
    }

    [Fact]
    public async Task A_failing_source_does_not_stop_the_others()
    {
        var client = _factory.ClientFor(Guid.NewGuid());
        _factory.Clock.Advance(TimeSpan.FromHours(7));
        _factory.Alpha.Fail = true;
        _factory.Beta.Offers.Add((Unique("b"), Unique("Backend engineer"), "Foo"));

        var results = (await Refresh(client)).GetProperty("results").EnumerateArray().ToDictionary(
            r => r.GetProperty("source").GetString()!, r => r);

        Assert.Equal("Failed", results["alpha"].GetProperty("outcome").GetString());
        Assert.Equal("Fetched", results["beta"].GetProperty("outcome").GetString());
        Assert.Equal(1, results["beta"].GetProperty("added").GetInt32());
    }

    [Fact]
    public async Task The_same_vacancy_published_by_two_sources_is_one_offer_that_names_both()
    {
        var ana = _factory.ClientFor(Guid.NewGuid());
        await ana.PutAsJsonAsync("/cv", CareersApiFactory.SampleCv());
        var title = Unique("Backend engineer");
        var company = Unique("Acme");
        _factory.Clock.Advance(TimeSpan.FromHours(7));
        _factory.Alpha.Offers.Add((Unique("a"), title, company));
        _factory.Beta.Offers.Add((Unique("b"), title.ToUpperInvariant(), company + "!"));

        await Refresh(ana, force: true);
        var offers = (await CareersApiFactory.Json(await ana.GetAsync("/offers?limit=500"))).GetProperty("offers");

        var matching = offers.EnumerateArray().Where(o => o.GetProperty("title").GetString() == title).ToList();
        Assert.Single(matching);
        Assert.Equal("beta", matching[0].GetProperty("alsoOn")[0].GetString());
        Assert.Equal("alpha", matching[0].GetProperty("source").GetString());
    }

    [Fact]
    public async Task Ranking_needs_a_saved_cv()
    {
        var response = await _factory.ClientFor(Guid.NewGuid()).GetAsync("/offers");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("cv-missing", (await CareersApiFactory.Json(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Offers_come_ranked_with_what_matched_where_to_apply_and_the_persons_own_status()
    {
        var ana = _factory.ClientFor(Guid.NewGuid());
        var luis = _factory.ClientFor(Guid.NewGuid());
        await ana.PutAsJsonAsync("/cv", CareersApiFactory.SampleCv());
        await luis.PutAsJsonAsync("/cv", CareersApiFactory.SampleCv("Luis"));
        var good = Unique("Backend engineer");
        var meh = Unique("Gardener");
        var senior = Unique("Senior backend engineer");
        _factory.Clock.Advance(TimeSpan.FromHours(7));
        _factory.Alpha.Offers.Add((Unique("a"), senior, "Foo"));
        _factory.Alpha.Offers.Add((Unique("a"), meh, "Foo"));
        _factory.Alpha.Offers.Add((Unique("a"), good, "Acme"));
        await Refresh(ana);

        var ranking = await CareersApiFactory.Json(await ana.GetAsync("/offers?limit=500"));
        var offers = ranking.GetProperty("offers").EnumerateArray().ToList();
        var top = offers.First(o => o.GetProperty("title").GetString() == good);
        Assert.True(top.GetProperty("score").GetDouble() > offers.First(o => o.GetProperty("title").GetString() == meh).GetProperty("score").GetDouble());
        Assert.Equal(".NET", top.GetProperty("matches")[0].GetString());
        // The experience an offer asks for comes with it, and is null when the offer does not say.
        Assert.Equal(JsonValueKind.Null, top.GetProperty("requiredYears").ValueKind);
        Assert.Equal(5, offers.First(o => o.GetProperty("title").GetString() == senior).GetProperty("requiredYears").GetInt32());
        Assert.StartsWith("https://alpha.example/", top.GetProperty("url").GetString());

        var offerId = top.GetProperty("id").GetGuid();
        await ana.PostAsJsonAsync("/applications", new { offerId });

        var mineAgain = (await CareersApiFactory.Json(await ana.GetAsync("/offers?limit=500"))).GetProperty("offers").EnumerateArray()
            .First(o => o.GetProperty("id").GetGuid() == offerId);
        Assert.Equal("Draft", mineAgain.GetProperty("applicationStatus").GetString());
        var theirs = (await CareersApiFactory.Json(await luis.GetAsync("/offers?limit=500"))).GetProperty("offers").EnumerateArray()
            .First(o => o.GetProperty("id").GetGuid() == offerId);
        Assert.Equal(JsonValueKind.Null, theirs.GetProperty("applicationStatus").ValueKind);
    }

    [Fact]
    public async Task Offers_the_ai_excluded_are_counted_not_listed_and_the_persons_rules_reach_it()
    {
        var ana = _factory.ClientFor(Guid.NewGuid());
        await ana.PutAsJsonAsync("/cv", CareersApiFactory.SampleCv());
        await ana.PutAsJsonAsync("/preferences", new SearchPreferences(["sales"], [], [], []));
        var sales = Unique("Sales rep");
        _factory.Clock.Advance(TimeSpan.FromHours(7));
        _factory.Alpha.Offers.Add((Unique("a"), sales, "Foo"));
        await Refresh(ana);

        var ranking = await CareersApiFactory.Json(await ana.GetAsync("/offers?limit=500"));

        Assert.True(ranking.GetProperty("excluded").GetInt32() >= 1);
        Assert.DoesNotContain(ranking.GetProperty("offers").EnumerateArray(), o => o.GetProperty("title").GetString() == sales);
        Assert.Equal("sales", _factory.Ai.LastPreferences!.ExcludeTitles[0]);
    }

    [Fact]
    public async Task The_experience_limit_reaches_the_ai_with_the_rest_of_the_persons_rules()
    {
        var ana = _factory.ClientFor(Guid.NewGuid());
        await ana.PutAsJsonAsync("/cv", CareersApiFactory.SampleCv());
        await ana.PutAsJsonAsync("/preferences", new SearchPreferences([], [], [], [], 3));
        _factory.Clock.Advance(TimeSpan.FromHours(7));
        _factory.Alpha.Offers.Add((Unique("a"), Unique("Backend developer"), "Foo"));
        await Refresh(ana);

        await ana.GetAsync("/offers?limit=500");

        Assert.Equal(3, _factory.Ai.LastPreferences!.MaxYears);
    }

    [Fact]
    public async Task Pending_affinity_calculations_are_reported_so_the_screen_can_ask_again()
    {
        var ana = _factory.ClientFor(Guid.NewGuid());
        await ana.PutAsJsonAsync("/cv", CareersApiFactory.SampleCv());
        _factory.Alpha.Offers.Add((Unique("a"), Unique("Backend engineer"), "Acme"));
        _factory.Clock.Advance(TimeSpan.FromHours(7));
        await Refresh(ana);
        _factory.Ai.Pending = 3;

        Assert.Equal(3, (await CareersApiFactory.Json(await ana.GetAsync("/offers"))).GetProperty("pending").GetInt32());
    }

    [Fact]
    public async Task The_limit_is_respected_and_bounded()
    {
        var ana = _factory.ClientFor(Guid.NewGuid());
        await ana.PutAsJsonAsync("/cv", CareersApiFactory.SampleCv());
        for (var i = 0; i < 3; i++)
        {
            _factory.Alpha.Offers.Add((Unique("a"), Unique("Backend engineer"), Unique("Co")));
        }

        _factory.Clock.Advance(TimeSpan.FromHours(7));
        await Refresh(ana);

        Assert.Equal(2, (await CareersApiFactory.Json(await ana.GetAsync("/offers?limit=2"))).GetProperty("offers").GetArrayLength());
        Assert.True((await CareersApiFactory.Json(await ana.GetAsync("/offers?limit=-5"))).GetProperty("offers").GetArrayLength() >= 1);
    }

    [Fact]
    public async Task When_the_ai_service_is_down_the_ranking_says_so_instead_of_failing_silently()
    {
        var ana = _factory.ClientFor(Guid.NewGuid());
        await ana.PutAsJsonAsync("/cv", CareersApiFactory.SampleCv());
        _factory.Alpha.Offers.Add((Unique("a"), Unique("Backend engineer"), "Acme"));
        _factory.Clock.Advance(TimeSpan.FromHours(7));
        await Refresh(ana);
        _factory.Ai.Down = true;
        try
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await ana.GetAsync("/offers")).StatusCode);
        }
        finally
        {
            _factory.Ai.Down = false;
        }
    }
}
