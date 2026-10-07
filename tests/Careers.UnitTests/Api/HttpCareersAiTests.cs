using System.Net;
using System.Text;
using System.Text.Json;
using Careers.Application;
using Careers.Domain.Offers;
using Careers.Infrastructure.Ai;
using Microsoft.Extensions.Logging.Abstractions;

namespace Careers.UnitTests.Api;

public class HttpCareersAiTests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add((request, request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct)));
            return respond(request);
        }
    }

    private static HttpCareersAi Client(Handler handler) => new(
        new HttpClient(handler) { BaseAddress = new Uri("http://ai.test/") }, NullLogger<HttpCareersAi>.Instance);

    private static JobOffer Offer() => JobOffer.Create(
        "a", "1", "https://x", "Backend", "Acme", "Worldwide", ["dotnet"], new string('d', 20000), null, DateTimeOffset.UtcNow);

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task The_experience_limit_travels_to_the_ai_in_its_own_field()
    {
        var handler = new Handler(_ => Json("""{"pendientes":0,"resultados":[]}"""));

        await Client(handler).RankAsync(Careers.UnitTests.Api.CareersApiFactory.SampleCv(), new Careers.Domain.Cv.SearchPreferences([], [], [], [], 3), [Offer()], default);

        using var sent = JsonDocument.Parse(handler.Calls[0].Body);
        Assert.Equal(3, sent.RootElement.GetProperty("preferencias").GetProperty("max_anos").GetInt32());
    }

    [Fact]
    public async Task The_cv_goes_to_the_ai_with_its_new_sections_and_pdf_switches()
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("%PDF"u8.ToArray()) });
        var cv = Careers.UnitTests.Api.CareersApiFactory.SampleCv() with
        {
            Languages = [new Careers.Domain.Cv.CvLanguage("English", "B2", Include: false)],
            Hidden = ["proyectos"],
        };

        await Client(handler).RenderPdfAsync(cv, default);

        using var sent = JsonDocument.Parse(handler.Calls[0].Body);
        var sentCv = sent.RootElement.GetProperty("cv");
        Assert.Equal("English", sentCv.GetProperty("idiomas")[0].GetProperty("nombre").GetString());
        Assert.False(sentCv.GetProperty("idiomas")[0].GetProperty("incluir").GetBoolean());
        Assert.Equal("proyectos", sentCv.GetProperty("ocultas")[0].GetString());
    }

    [Fact]
    public async Task Import_sends_the_raw_file_and_brings_the_draft_back_in_this_systems_shape()
    {
        var handler = new Handler(_ => Json("""
        {"cv":{"nombre":"Ana","titular":"T","resumen":"R","contacto":{"email":"a@b.co","telefono":null,"ubicacion":null,"enlaces":{}},
         "experiencia":[],"proyectos":[],"educacion":[],"habilidades":[],
         "referencias":[{"nombre":"Luis","cargo":"Lead","contacto":"099","incluir":true}],"ocultas":["referencias"]},
         "origen":"pdf","avisos":["no-verificado:2"]}
        """));

        var result = await Client(handler).ImportAsync([1, 2, 3], default);

        Assert.Equal("pdf", result.Source);
        Assert.Equal("Ana", result.Cv.FullName);
        Assert.Equal("Luis", result.Cv.References[0].Name);
        Assert.Equal(["referencias"], result.Cv.Hidden);
        Assert.Equal(["no-verificado:2"], result.Warnings);
        Assert.EndsWith("v1/import", handler.Calls[0].Request.RequestUri!.AbsolutePath);
        Assert.Equal("application/octet-stream", handler.Calls[0].Request.Content!.Headers.ContentType!.MediaType);
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity, """{"detail":"sin-texto"}""", "sin-texto")]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, """{"detail":"demasiado-grande"}""", "demasiado-grande")]
    [InlineData(HttpStatusCode.UnprocessableEntity, """{"detail":"<script>alert(1)</script>"}""", "ilegible")] // only a short slug is trusted
    [InlineData(HttpStatusCode.UnprocessableEntity, "not json", "ilegible")]
    public async Task A_file_careers_ai_rejects_becomes_a_reason_not_a_service_failure(HttpStatusCode status, string body, string reason)
    {
        var handler = new Handler(_ => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });

        var failure = await Assert.ThrowsAsync<ImportFailedException>(() => Client(handler).ImportAsync([1], default));

        Assert.Equal(reason, failure.Reason);
    }

    [Fact]
    public async Task A_server_error_while_importing_is_the_service_being_unavailable()
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await Assert.ThrowsAsync<AiUnavailableException>(() => Client(handler).ImportAsync([1], default));
    }

    [Fact]
    public async Task Rank_speaks_the_ai_contract_and_trims_oversized_offers()
    {
        var offer = Offer();
        var handler = new Handler(_ => Json($$"""{"pendientes":2,"resultados":[{"id":"{{offer.Id}}","puntaje":88.5,"excluida":null,"coinciden":[".NET"]},{"id":"not-a-guid","puntaje":1,"excluida":null,"coinciden":[]}]}"""));

        var result = await Client(handler).RankAsync(Careers.UnitTests.Api.CareersApiFactory.SampleCv(), Careers.Domain.Cv.SearchPreferences.None, [offer], default);

        Assert.Equal(2, result.Pending);
        var ranked = Assert.Single(result.Offers); // an id this system did not issue is ignored
        Assert.Equal(offer.Id, ranked.OfferId);
        Assert.Equal(88.5, ranked.Score);
        using var sent = JsonDocument.Parse(handler.Calls[0].Body);
        Assert.Equal("Ana Example", sent.RootElement.GetProperty("cv").GetProperty("nombre").GetString());
        Assert.Equal(JsonValueKind.Null, sent.RootElement.GetProperty("preferencias").GetProperty("max_anos").ValueKind);
        Assert.Equal(12000, sent.RootElement.GetProperty("ofertas")[0].GetProperty("descripcion").GetString()!.Length);
        Assert.EndsWith("v1/rank", handler.Calls[0].Request.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Adapt_brings_the_cv_back_in_this_systems_shape()
    {
        var handler = new Handler(_ => Json("""
        {"cv":{"nombre":"Ana","titular":"T","resumen":"R","contacto":{"email":"a@b.co","telefono":null,"ubicacion":null,"enlaces":{}},
         "experiencia":[{"cargo":"Dev","organizacion":"Acme","desde":"2022","hasta":null,"logros":["x"],"tecnologias":[".NET"]}],
         "proyectos":[],"educacion":[],"habilidades":[{"nombre":"Destacadas","items":[".NET"]}]},"cambiado":true}
        """));

        var cv = await Client(handler).AdaptAsync(CareersApiFactory.SampleCv(), Offer(), default);

        Assert.Equal("Dev", cv.Experience[0].Role);
        Assert.Equal("Destacadas", cv.Skills[0].Name);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task Any_failure_from_the_ai_becomes_one_clear_error(HttpStatusCode status)
    {
        var handler = new Handler(_ => new HttpResponseMessage(status));

        await Assert.ThrowsAsync<AiUnavailableException>(() => Client(handler).RenderPdfAsync(CareersApiFactory.SampleCv(), default));
    }

    [Fact]
    public async Task An_unreachable_or_garbled_ai_becomes_the_same_error()
    {
        await Assert.ThrowsAsync<AiUnavailableException>(() =>
            Client(new Handler(_ => throw new HttpRequestException("refused"))).LetterAsync(CareersApiFactory.SampleCv(), Offer(), default));
        await Assert.ThrowsAsync<AiUnavailableException>(() =>
            Client(new Handler(_ => Json("not json"))).LetterAsync(CareersApiFactory.SampleCv(), Offer(), default));
    }
}
