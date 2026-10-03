using System.Net;
using System.Net.Http.Json;
using System.Text;
using Careers.Domain.Cv;

namespace Careers.UnitTests.Api;

public class CvEndpointsTests : IClassFixture<CareersApiFactory>
{
    private readonly CareersApiFactory _factory;

    public CvEndpointsTests(CareersApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Every_endpoint_needs_a_signed_in_person_with_a_token_from_the_same_issuer()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.ClientFor().GetAsync("/cv")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.ClientFor().GetAsync("/offers")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.ClientFor().GetAsync("/applications")).StatusCode);

        var foreign = _factory.CreateClient();
        foreign.DefaultRequestHeaders.Authorization = new("Bearer", CareersApiFactory.Token(Guid.NewGuid(), issuer: "someone-else"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await foreign.GetAsync("/cv")).StatusCode);

        var forged = _factory.CreateClient();
        forged.DefaultRequestHeaders.Authorization = new("Bearer", CareersApiFactory.Token(Guid.NewGuid(), key: "another-signing-key-another-signing-key-99"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await forged.GetAsync("/cv")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await _factory.ClientFor().GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task A_token_whose_subject_is_not_a_user_id_is_rejected()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", CareersApiFactory.Token("not-a-guid"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/cv")).StatusCode);
    }

    [Fact]
    public async Task A_new_person_gets_an_empty_cv_that_is_not_saved_yet()
    {
        var body = await CareersApiFactory.Json(await _factory.ClientFor(Guid.NewGuid()).GetAsync("/cv"));

        Assert.False(body.GetProperty("saved").GetBoolean());
        Assert.Equal(string.Empty, body.GetProperty("cv").GetProperty("fullName").GetString());
    }

    [Fact]
    public async Task A_saved_cv_comes_back_the_same_and_belongs_only_to_its_owner()
    {
        var ana = _factory.ClientFor(Guid.NewGuid());
        var luis = _factory.ClientFor(Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NoContent, (await ana.PutAsJsonAsync("/cv", CareersApiFactory.SampleCv("Ana"))).StatusCode);

        var mine = await CareersApiFactory.Json(await ana.GetAsync("/cv"));
        Assert.True(mine.GetProperty("saved").GetBoolean());
        Assert.Equal("Ana", mine.GetProperty("cv").GetProperty("fullName").GetString());
        Assert.Equal("Cut a query from 8 s to 300 ms",
            mine.GetProperty("cv").GetProperty("experience")[0].GetProperty("achievements")[0].GetString());

        var theirs = await CareersApiFactory.Json(await luis.GetAsync("/cv"));
        Assert.False(theirs.GetProperty("saved").GetBoolean());
    }

    [Fact]
    public async Task Saving_again_replaces_the_cv_instead_of_adding_one()
    {
        var client = _factory.ClientFor(Guid.NewGuid());
        await client.PutAsJsonAsync("/cv", CareersApiFactory.SampleCv("First"));
        await client.PutAsJsonAsync("/cv", CareersApiFactory.SampleCv("Second"));

        var body = await CareersApiFactory.Json(await client.GetAsync("/cv"));
        Assert.Equal("Second", body.GetProperty("cv").GetProperty("fullName").GetString());
    }

    [Fact]
    public async Task Invalid_cvs_are_refused_with_the_fields_that_failed()
    {
        var client = _factory.ClientFor(Guid.NewGuid());
        var bad = CareersApiFactory.SampleCv(" ") with
        {
            Contact = new CvContact("not-an-email", new Dictionary<string, string> { ["x"] = "javascript:alert(1)" }),
            Projects = [new CvProject("P", "d", [], "ftp://example.com")],
        };

        var response = await client.PutAsJsonAsync("/cv", bad);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await CareersApiFactory.Json(response);
        Assert.Equal("validation", body.GetProperty("code").GetString());
        var errors = body.GetProperty("errors");
        Assert.True(errors.TryGetProperty("fullName", out _));
        Assert.True(errors.TryGetProperty("contact.email", out _));
        Assert.True(errors.TryGetProperty("contact.links", out _));
        Assert.True(errors.TryGetProperty("projects[0].url", out _));
    }

    [Fact]
    public async Task Oversized_cvs_are_refused()
    {
        var client = _factory.ClientFor(Guid.NewGuid());
        var huge = CareersApiFactory.SampleCv() with { Summary = new string('x', 2001) };

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PutAsJsonAsync("/cv", huge)).StatusCode);
    }

    [Fact]
    public async Task A_body_missing_required_fields_is_a_client_error_not_a_crash()
    {
        var client = _factory.ClientFor(Guid.NewGuid());
        var response = await client.PutAsync("/cv", new StringContent("""{"fullName":"Ana"}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_pdf_of_a_draft_is_rendered_without_saving_it()
    {
        var client = _factory.ClientFor(Guid.NewGuid());

        var response = await client.PostAsJsonAsync("/cv/pdf", CareersApiFactory.SampleCv("Draft"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("%PDF-fake", await response.Content.ReadAsStringAsync());
        Assert.Equal("Draft", _factory.Ai.LastRendered!.FullName);
        Assert.False((await CareersApiFactory.Json(await client.GetAsync("/cv"))).GetProperty("saved").GetBoolean());
    }

    [Fact]
    public async Task Without_a_draft_the_pdf_is_the_saved_cv_and_needs_one()
    {
        var client = _factory.ClientFor(Guid.NewGuid());
        var none = await client.PostAsync("/cv/pdf", null);
        Assert.Equal(HttpStatusCode.Conflict, none.StatusCode);
        Assert.Equal("cv-missing", (await CareersApiFactory.Json(none)).GetProperty("code").GetString());

        await client.PutAsJsonAsync("/cv", CareersApiFactory.SampleCv("Saved"));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/cv/pdf", null)).StatusCode);
        Assert.Equal("Saved", _factory.Ai.LastRendered!.FullName);
    }

    [Fact]
    public async Task When_the_ai_service_is_down_the_pdf_fails_cleanly()
    {
        var client = _factory.ClientFor(Guid.NewGuid());
        _factory.Ai.Down = true;
        try
        {
            var response = await client.PostAsJsonAsync("/cv/pdf", CareersApiFactory.SampleCv());

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("ai-unavailable", (await CareersApiFactory.Json(response)).GetProperty("code").GetString());
        }
        finally
        {
            _factory.Ai.Down = false;
        }
    }

    [Fact]
    public async Task Preferences_are_per_person_and_cleaned_up()
    {
        var ana = _factory.ClientFor(Guid.NewGuid());
        var luis = _factory.ClientFor(Guid.NewGuid());

        await ana.PutAsJsonAsync("/preferences", new SearchPreferences([" sales ", "Sales", ""], [], ["worldwide"], []));

        var mine = await CareersApiFactory.Json(await ana.GetAsync("/preferences"));
        Assert.Equal(1, mine.GetProperty("excludeTitles").GetArrayLength());
        Assert.Equal("sales", mine.GetProperty("excludeTitles")[0].GetString());
        Assert.Equal(0, (await CareersApiFactory.Json(await luis.GetAsync("/preferences"))).GetProperty("excludeTitles").GetArrayLength());
    }

    [Fact]
    public async Task Preferences_have_limits()
    {
        var client = _factory.ClientFor(Guid.NewGuid());
        var tooMany = new SearchPreferences(Enumerable.Range(0, 31).Select(i => $"t{i}").ToList(), [], [], []);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PutAsJsonAsync("/preferences", tooMany)).StatusCode);
    }
}
