using Careers.Api.Auth;
using Careers.Application.Cv;
using Careers.Domain.Cv;

namespace Careers.Api.Endpoints;

public static class CvEndpoints
{
    public const string HeavyPolicy = "heavy";

    public static void MapCvEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/cv", async (HttpContext http, CvService cvs, CancellationToken ct) =>
        {
            var (cv, saved) = await cvs.GetAsync(http.User.UserId(), ct);
            return Results.Ok(new { cv, saved });
        });

        app.MapPut("/cv", async (HttpContext http, CvDocument cv, CvService cvs, CancellationToken ct) =>
        {
            await cvs.SaveAsync(http.User.UserId(), cv, ct);
            return Results.NoContent();
        });

        // Compiles a PDF: the draft in the body (nothing is stored) or, with no body, the saved CV.
        app.MapPost("/cv/pdf", async (HttpContext http, CvService cvs, CancellationToken ct) =>
        {
            CvDocument? draft = null;
            if (http.Request.ContentLength is > 0)
            {
                draft = await http.Request.ReadFromJsonAsync<CvDocument>(Careers.Application.CareersJson.Options, ct);
            }

            return Results.File(await cvs.RenderAsync(http.User.UserId(), draft, ct), "application/pdf");
        }).RequireRateLimiting(HeavyPolicy);

        app.MapGet("/preferences", async (HttpContext http, CvService cvs, CancellationToken ct) =>
            Results.Ok(await cvs.GetPreferencesAsync(http.User.UserId(), ct)));

        app.MapPut("/preferences", async (HttpContext http, SearchPreferences preferences, CvService cvs, CancellationToken ct) =>
        {
            await cvs.SavePreferencesAsync(http.User.UserId(), preferences, ct);
            return Results.NoContent();
        });
    }
}
