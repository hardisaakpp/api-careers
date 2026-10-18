using Careers.Api.Auth;
using Careers.Application;
using Careers.Application.Cv;
using Careers.Domain.Cv;

namespace Careers.Api.Endpoints;

public static class CvEndpoints
{
    public const string HeavyPolicy = "heavy";

    public static void MapCvEndpoints(this IEndpointRouteBuilder app)
    {
        // `?lang=` picks the version of the CV (es, the default, or en): each is stored, edited and rendered on its own.
        app.MapGet("/cv", async (HttpContext http, CvService cvs, CancellationToken ct, string lang = CvLanguages.Spanish) =>
        {
            var (cv, saved) = await cvs.GetAsync(http.User.UserId(), lang, ct);
            return Results.Ok(new { cv, saved });
        });

        app.MapPut("/cv", async (HttpContext http, CvDocument cv, CvService cvs, CancellationToken ct, string lang = CvLanguages.Spanish) =>
        {
            await cvs.SaveAsync(http.User.UserId(), lang, cv, ct);
            return Results.NoContent();
        });

        // An English draft of the saved Spanish CV: nothing is stored, the person edits it and saves it with PUT /cv?lang=en.
        app.MapPost("/cv/translate", async (HttpContext http, CvService cvs, CancellationToken ct) =>
        {
            var translated = await cvs.TranslateAsync(http.User.UserId(), ct);
            return Results.Ok(new { cv = translated.Cv, warnings = translated.Warnings });
        }).RequireRateLimiting(HeavyPolicy);

        // Compiles a PDF: the draft in the body (nothing is stored) or, with no body, the saved CV.
        app.MapPost("/cv/pdf", async (HttpContext http, CvService cvs, CancellationToken ct, string lang = CvLanguages.Spanish) =>
        {
            CvDocument? draft = null;
            if (http.Request.ContentLength is > 0)
            {
                draft = await http.Request.ReadFromJsonAsync<CvDocument>(Careers.Application.CareersJson.Options, ct);
            }

            return Results.File(await cvs.RenderAsync(http.User.UserId(), lang, draft, ct), "application/pdf");
        }).RequireRateLimiting(HeavyPolicy);

        // Reads an existing CV (PDF, DOCX or a LinkedIn data export) into a draft. The file travels raw in the body.
        app.MapPost("/cv/import", async (HttpContext http, CvService cvs, CancellationToken ct) =>
        {
            var imported = await cvs.ImportAsync(await ReadFileAsync(http.Request, ct), ct);
            return Results.Ok(new { cv = imported.Cv, source = imported.Source, warnings = imported.Warnings });
        }).RequireRateLimiting(HeavyPolicy);

        app.MapGet("/preferences", async (HttpContext http, CvService cvs, CancellationToken ct) =>
            Results.Ok(await cvs.GetPreferencesAsync(http.User.UserId(), ct)));

        app.MapPut("/preferences", async (HttpContext http, SearchPreferences preferences, CvService cvs, CancellationToken ct) =>
        {
            await cvs.SavePreferencesAsync(http.User.UserId(), preferences, ct);
            return Results.NoContent();
        });
    }

    public const int MaxImportBytes = 5 * 1024 * 1024;

    /// <summary>The raw body, refused as soon as it is over the limit (the header first, then while reading: a chunked body has no header).</summary>
    private static async Task<byte[]> ReadFileAsync(HttpRequest request, CancellationToken ct)
    {
        if (request.ContentLength > MaxImportBytes)
        {
            throw new ImportFailedException("demasiado-grande");
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxImportBytes)
            {
                throw new ImportFailedException("demasiado-grande");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.Length == 0 ? throw new ImportFailedException("vacio") : buffer.ToArray();
    }
}
