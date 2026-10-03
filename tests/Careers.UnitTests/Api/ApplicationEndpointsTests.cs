using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Careers.UnitTests.Api;

public class ApplicationEndpointsTests : IClassFixture<CareersApiFactory>
{
    private readonly CareersApiFactory _factory;

    public ApplicationEndpointsTests(CareersApiFactory factory) => _factory = factory;

    private async Task<(HttpClient Client, Guid OfferId)> PersonWithAnOffer()
    {
        var client = _factory.ClientFor(Guid.NewGuid());
        await client.PutAsJsonAsync("/cv", CareersApiFactory.SampleCv());
        _factory.Alpha.Offers.Clear();
        _factory.Alpha.Offers.Add(($"id-{Guid.NewGuid():N}", $"Backend engineer {Guid.NewGuid():N}", "Acme"));
        _factory.Clock.Advance(TimeSpan.FromHours(7));
        await client.PostAsync("/offers/refresh?force=true", null);
        var offers = (await CareersApiFactory.Json(await client.GetAsync("/offers?limit=500"))).GetProperty("offers");
        var title = _factory.Alpha.Offers[0].Title;
        return (client, offers.EnumerateArray().First(o => o.GetProperty("title").GetString() == title).GetProperty("id").GetGuid());
    }

    private static async Task<Guid> Prepare(HttpClient client, Guid offerId) =>
        (await CareersApiFactory.Json(await client.PostAsJsonAsync("/applications", new { offerId }))).GetProperty("id").GetGuid();

    [Fact]
    public async Task Preparing_an_application_tailors_the_cv_writes_a_letter_and_starts_as_a_draft()
    {
        var (client, offerId) = await PersonWithAnOffer();

        var response = await client.PostAsJsonAsync("/applications", new { offerId });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await CareersApiFactory.Json(response)).GetProperty("id").GetGuid();
        var detail = await CareersApiFactory.Json(await client.GetAsync($"/applications/{id}"));
        Assert.Equal("Draft", detail.GetProperty("status").GetString());
        Assert.StartsWith("Dear Acme", detail.GetProperty("coverLetter").GetString());
        Assert.Equal("Acme", detail.GetProperty("offer").GetProperty("company").GetString());
    }

    [Fact]
    public async Task Preparing_twice_returns_the_same_draft_and_calls_the_ai_once()
    {
        var (client, offerId) = await PersonWithAnOffer();
        var calls = _factory.Ai.AdaptCalls;

        var first = await Prepare(client, offerId);
        var secondResponse = await client.PostAsJsonAsync("/applications", new { offerId });

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Equal(first, (await CareersApiFactory.Json(secondResponse)).GetProperty("id").GetGuid());
        Assert.Equal(calls + 1, _factory.Ai.AdaptCalls);
    }

    [Fact]
    public async Task Preparing_needs_a_real_offer_and_a_saved_cv()
    {
        var (client, offerId) = await PersonWithAnOffer();
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/applications", new { offerId = Guid.NewGuid() })).StatusCode);

        var noCv = _factory.ClientFor(Guid.NewGuid());
        var response = await noCv.PostAsJsonAsync("/applications", new { offerId });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("cv-missing", (await CareersApiFactory.Json(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task When_the_ai_is_down_nothing_is_saved()
    {
        var (client, offerId) = await PersonWithAnOffer();
        _factory.Ai.Down = true;
        try
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync("/applications", new { offerId })).StatusCode);
        }
        finally
        {
            _factory.Ai.Down = false;
        }

        Assert.Equal(0, (await CareersApiFactory.Json(await client.GetAsync("/applications"))).GetArrayLength());
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/applications", new { offerId })).StatusCode);
    }

    [Fact]
    public async Task An_application_can_only_move_forward_through_confirmation_and_never_back()
    {
        var (client, offerId) = await PersonWithAnOffer();
        var id = await Prepare(client, offerId);

        var early = await client.PostAsync($"/applications/{id}/sent", null); // cannot skip confirming
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        Assert.Equal("invalid-transition", (await CareersApiFactory.Json(early)).GetProperty("code").GetString());

        Assert.Equal("Confirmed", (await CareersApiFactory.Json(await client.PostAsync($"/applications/{id}/confirm", null))).GetProperty("status").GetString());
        Assert.Equal("Sent", (await CareersApiFactory.Json(await client.PostAsync($"/applications/{id}/sent", null))).GetProperty("status").GetString());

        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/applications/{id}/discard", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/applications/{id}/confirm", null)).StatusCode);
    }

    [Fact]
    public async Task A_draft_can_be_discarded_and_then_nothing_else_changes()
    {
        var (client, offerId) = await PersonWithAnOffer();
        var id = await Prepare(client, offerId);

        Assert.Equal("Discarded", (await CareersApiFactory.Json(await client.PostAsync($"/applications/{id}/discard", null))).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/applications/{id}/confirm", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/applications/{id}/letter", new { coverLetter = "x" })).StatusCode);
    }

    [Fact]
    public async Task The_letter_can_be_edited_while_it_is_still_open_within_limits()
    {
        var (client, offerId) = await PersonWithAnOffer();
        var id = await Prepare(client, offerId);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/applications/{id}/letter", new { coverLetter = "My own words." })).StatusCode);
        Assert.Equal("My own words.", (await CareersApiFactory.Json(await client.GetAsync($"/applications/{id}"))).GetProperty("coverLetter").GetString());

        var tooLong = await client.PutAsJsonAsync($"/applications/{id}/letter", new { coverLetter = new string('x', 8001) });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooLong.StatusCode);
    }

    [Fact]
    public async Task Nobody_can_see_or_change_someone_elses_application()
    {
        var (ana, offerId) = await PersonWithAnOffer();
        var id = await Prepare(ana, offerId);
        var intruder = _factory.ClientFor(Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, (await intruder.GetAsync($"/applications/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.GetAsync($"/applications/{id}/cv.pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.PostAsync($"/applications/{id}/confirm", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.PutAsJsonAsync($"/applications/{id}/letter", new { coverLetter = "hijacked" })).StatusCode);
        Assert.Equal(0, (await CareersApiFactory.Json(await intruder.GetAsync("/applications"))).GetArrayLength());
        Assert.Equal("Draft", (await CareersApiFactory.Json(await ana.GetAsync($"/applications/{id}"))).GetProperty("status").GetString());
    }

    [Fact]
    public async Task The_application_lists_only_the_persons_own_with_where_to_apply()
    {
        var (client, offerId) = await PersonWithAnOffer();
        var id = await Prepare(client, offerId);

        var list = (await CareersApiFactory.Json(await client.GetAsync("/applications"))).EnumerateArray().ToList();

        Assert.Single(list);
        Assert.Equal(id, list[0].GetProperty("id").GetGuid());
        Assert.StartsWith("https://alpha.example/", list[0].GetProperty("url").GetString());
    }

    [Fact]
    public async Task The_pdf_is_the_cv_that_was_tailored_for_that_offer_not_the_current_one()
    {
        var (client, offerId) = await PersonWithAnOffer();
        var id = await Prepare(client, offerId);
        await client.PutAsJsonAsync("/cv", CareersApiFactory.SampleCv("Renamed Later"));

        var pdf = await client.GetAsync($"/applications/{id}/cv.pdf");

        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal("Ana Example", _factory.Ai.LastRendered!.FullName);
        Assert.EndsWith("(tailored)", _factory.Ai.LastRendered.Headline);
    }

    [Fact]
    public async Task Malformed_ids_are_not_found_not_errors()
    {
        var client = _factory.ClientFor(Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/applications/not-a-guid")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/applications", JsonContent.Create(new { offerId = "nope" }))).StatusCode);
    }
}
