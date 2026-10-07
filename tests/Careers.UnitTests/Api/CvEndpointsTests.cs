using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
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
    public async Task A_cv_saved_before_the_new_sections_existed_still_loads_with_them_empty()
    {
        var client = _factory.ClientFor(Guid.NewGuid());
        const string legacy = """
            {"fullName":"Ana","headline":"","summary":"","contact":{"email":"","links":{}},
             "experience":[{"role":"Dev","organization":"Acme","from":"2022","achievements":[],"technologies":[]}],
             "projects":[],"education":[],"skills":[]}
            """;

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync("/cv", new StringContent(legacy, Encoding.UTF8, "application/json"))).StatusCode);

        var cv = (await CareersApiFactory.Json(await client.GetAsync("/cv"))).GetProperty("cv");
        Assert.Equal(0, cv.GetProperty("certifications").GetArrayLength());
        Assert.Equal(0, cv.GetProperty("languages").GetArrayLength());
        Assert.Equal(0, cv.GetProperty("research").GetArrayLength());
        Assert.Equal(0, cv.GetProperty("hidden").GetArrayLength());
        Assert.True(cv.GetProperty("experience")[0].GetProperty("include").GetBoolean()); // shown unless switched off
    }

    [Fact]
    public async Task The_new_sections_and_the_pdf_switches_are_stored_and_sent_to_the_renderer()
    {
        var client = _factory.ClientFor(Guid.NewGuid());
        var full = CareersApiFactory.SampleCv("Ana") with
        {
            Certifications = [new CvCertification("Scrum Foundation", "Certiprof", "2024"), new CvCertification("Old", Include: false)],
            Languages = [new CvLanguage("English", "B2")],
            Research = [new CvExperience("Researcher", "Smart Lab", "2025-10", ["RAG pipeline"], [], Location: "Quito")],
            Hidden = ["resumen", "contacto.telefono", "contacto.iconos"],
        };

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync("/cv", full)).StatusCode);

        var cv = (await CareersApiFactory.Json(await client.GetAsync("/cv"))).GetProperty("cv");
        Assert.False(cv.GetProperty("certifications")[1].GetProperty("include").GetBoolean());
        Assert.Equal("Quito", cv.GetProperty("research")[0].GetProperty("location").GetString());
        Assert.Equal("contacto.telefono", cv.GetProperty("hidden")[1].GetString());
        Assert.Equal("contacto.iconos", cv.GetProperty("hidden")[2].GetString());

        await client.PostAsync("/cv/pdf", null);
        Assert.Equal(["resumen", "contacto.telefono", "contacto.iconos"], _factory.Ai.LastRendered!.Hidden);
        Assert.False(_factory.Ai.LastRendered.Certifications[1].Include);
    }

    [Fact]
    public async Task An_unknown_pdf_switch_is_refused()
    {
        var client = _factory.ClientFor(Guid.NewGuid());
        var response = await client.PutAsJsonAsync("/cv", CareersApiFactory.SampleCv() with { Hidden = ["not-a-section"] });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.True((await CareersApiFactory.Json(response)).GetProperty("errors").TryGetProperty("hidden", out _));
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

    [Fact]
    public async Task The_experience_limit_is_stored_and_a_person_without_one_has_no_limit()
    {
        var client = _factory.ClientFor(Guid.NewGuid());
        Assert.Equal(JsonValueKind.Null, (await CareersApiFactory.Json(await client.GetAsync("/preferences"))).GetProperty("maxYears").ValueKind);

        await client.PutAsJsonAsync("/preferences", new SearchPreferences([], [], [], [], 3));
        Assert.Equal(3, (await CareersApiFactory.Json(await client.GetAsync("/preferences"))).GetProperty("maxYears").GetInt32());

        // Clearing it (null) means no limit again.
        await client.PutAsJsonAsync("/preferences", new SearchPreferences([], [], [], [], null));
        Assert.Equal(JsonValueKind.Null, (await CareersApiFactory.Json(await client.GetAsync("/preferences"))).GetProperty("maxYears").ValueKind);
    }

    [Fact]
    public async Task Preferences_saved_before_the_limit_existed_still_load_and_save()
    {
        var client = _factory.ClientFor(Guid.NewGuid());
        const string legacy = """{"excludeTitles":["sales"],"excludeText":[],"locations":[],"requiredTechnologies":[]}""";

        var saved = await client.PutAsync("/preferences", new StringContent(legacy, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        var back = await CareersApiFactory.Json(await client.GetAsync("/preferences"));
        Assert.Equal("sales", back.GetProperty("excludeTitles")[0].GetString());
        Assert.Equal(JsonValueKind.Null, back.GetProperty("maxYears").ValueKind);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(31)]
    public async Task An_unreasonable_experience_limit_is_refused(int years)
    {
        var response = await _factory.ClientFor(Guid.NewGuid()).PutAsJsonAsync("/preferences", new SearchPreferences([], [], [], [], years));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.True((await CareersApiFactory.Json(response)).GetProperty("errors").TryGetProperty("maxYears", out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    public async Task The_edges_of_the_range_are_allowed(int years)
    {
        var response = await _factory.ClientFor(Guid.NewGuid()).PutAsJsonAsync("/preferences", new SearchPreferences([], [], [], [], years));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static ByteArrayContent File(byte[] bytes) =>
        new(bytes) { Headers = { ContentType = new("application/octet-stream") } };

    [Fact]
    public async Task Importing_a_file_returns_a_draft_and_stores_nothing()
    {
        var client = _factory.ClientFor(Guid.NewGuid());
        _factory.Ai.ImportFailure = null;
        _factory.Ai.NextImport = new Careers.Application.CvImport(
            CareersApiFactory.SampleCv("Imported Ana"), "linkedin", ["linkedin-sin-logros"]);

        var response = await client.PostAsync("/cv/import", File("%PDF-1.4 bytes"u8.ToArray()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await CareersApiFactory.Json(response);
        Assert.Equal("linkedin", body.GetProperty("source").GetString());
        Assert.Equal("Imported Ana", body.GetProperty("cv").GetProperty("fullName").GetString());
        Assert.Equal("linkedin-sin-logros", body.GetProperty("warnings")[0].GetString());
        Assert.False((await CareersApiFactory.Json(await client.GetAsync("/cv"))).GetProperty("saved").GetBoolean());
        Assert.Equal("%PDF-1.4 bytes", System.Text.Encoding.UTF8.GetString(_factory.Ai.LastImported!));
    }

    [Fact]
    public async Task An_imported_draft_always_fits_the_limits_so_it_can_be_saved_as_reviewed()
    {
        var client = _factory.ClientFor(Guid.NewGuid());
        _factory.Ai.ImportFailure = null;
        var wild = CareersApiFactory.SampleCv(new string('N', 500)) with
        {
            Summary = new string('s', 5000),
            Contact = new CvContact("not-an-email", new Dictionary<string, string> { ["ok"] = "https://ok.example", ["bad"] = "ftp://x" }, new string('9', 99)),
            Languages = [.. Enumerable.Range(0, 40).Select(i => new CvLanguage($"L{i}"))],
            Hidden = ["resumen", "not-a-section"],
        };
        _factory.Ai.NextImport = new Careers.Application.CvImport(wild, "pdf", []);

        var draft = await CareersApiFactory.Json(await client.PostAsync("/cv/import", File([1])));
        var cv = draft.GetProperty("cv");

        Assert.Equal(120, cv.GetProperty("fullName").GetString()!.Length);
        Assert.Equal(2000, cv.GetProperty("summary").GetString()!.Length);
        Assert.Equal(string.Empty, cv.GetProperty("contact").GetProperty("email").GetString());
        Assert.True(cv.GetProperty("contact").GetProperty("links").TryGetProperty("ok", out _));
        Assert.False(cv.GetProperty("contact").GetProperty("links").TryGetProperty("bad", out _));
        Assert.Equal(15, cv.GetProperty("languages").GetArrayLength());
        Assert.Equal(1, cv.GetProperty("hidden").GetArrayLength());
        // What the person reviews can be saved exactly as it came.
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync("/cv", cv)).StatusCode);
    }

    [Theory]
    [InlineData("sin-texto", HttpStatusCode.UnprocessableEntity)]
    [InlineData("formato-desconocido", HttpStatusCode.UnprocessableEntity)]
    [InlineData("demasiado-grande", HttpStatusCode.RequestEntityTooLarge)]
    public async Task A_file_that_cannot_be_read_says_why_with_a_stable_code(string reason, HttpStatusCode status)
    {
        _factory.Ai.ImportFailure = reason;
        try
        {
            var response = await _factory.ClientFor(Guid.NewGuid()).PostAsync("/cv/import", File([1, 2, 3]));

            Assert.Equal(status, response.StatusCode);
            Assert.Equal($"import-{reason}", (await CareersApiFactory.Json(response)).GetProperty("code").GetString());
        }
        finally
        {
            _factory.Ai.ImportFailure = null;
        }
    }

    [Fact]
    public async Task An_empty_or_oversized_upload_is_refused_before_reaching_the_ai()
    {
        var client = _factory.ClientFor(Guid.NewGuid());
        _factory.Ai.ImportFailure = null;

        var empty = await client.PostAsync("/cv/import", File([]));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, empty.StatusCode);
        Assert.Equal("import-vacio", (await CareersApiFactory.Json(empty)).GetProperty("code").GetString());

        var calls = _factory.Ai.LastImported;
        var big = await client.PostAsync("/cv/import", File(new byte[Careers.Api.Endpoints.CvEndpoints.MaxImportBytes + 1]));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, big.StatusCode);
        Assert.Same(calls, _factory.Ai.LastImported);
    }

    [Fact]
    public async Task Importing_needs_a_signed_in_person_and_a_working_ai_service()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.ClientFor().PostAsync("/cv/import", File([1]))).StatusCode);

        _factory.Ai.Down = true;
        try
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await _factory.ClientFor(Guid.NewGuid()).PostAsync("/cv/import", File([1]))).StatusCode);
        }
        finally
        {
            _factory.Ai.Down = false;
        }
    }

    [Fact]
    public async Task References_are_stored_and_can_be_switched_out_of_the_pdf()
    {
        var client = _factory.ClientFor(Guid.NewGuid());
        var cv = CareersApiFactory.SampleCv("Ana") with
        {
            References = [new CvReference("Luis Perez", "Lead", "099")],
            Hidden = ["referencias"],
        };

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync("/cv", cv)).StatusCode);

        var back = (await CareersApiFactory.Json(await client.GetAsync("/cv"))).GetProperty("cv");
        Assert.Equal("Luis Perez", back.GetProperty("references")[0].GetProperty("name").GetString());
        Assert.Equal("referencias", back.GetProperty("hidden")[0].GetString());
    }
}
