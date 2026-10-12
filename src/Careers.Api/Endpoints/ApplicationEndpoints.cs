using Careers.Api.Auth;
using Careers.Application.Applications;
using Careers.Domain.Offers;

namespace Careers.Api.Endpoints;

public sealed record LetterRequest(string CoverLetter);

public sealed record PrepareRequest(Guid OfferId);

public static class ApplicationEndpoints
{
    public static void MapApplicationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/applications");

        group.MapPost("/", async (HttpContext http, PrepareRequest request, ApplicationService applications, CancellationToken ct) =>
        {
            var (id, created) = await applications.PrepareAsync(http.User.UserId(), request.OfferId, ct);
            return created ? Results.Created($"/applications/{id}", new { id }) : Results.Ok(new { id });
        }).RequireRateLimiting(CvEndpoints.HeavyPolicy);

        group.MapGet("/", async (HttpContext http, ApplicationService applications, CancellationToken ct) =>
            Results.Ok((await applications.ListAsync(http.User.UserId(), ct)).Select(d => new
            {
                d.Application.Id,
                d.Application.Status,
                OfferId = d.Offer.Id,
                d.Offer.Title,
                d.Offer.Company,
                d.Offer.Url,
                d.Application.UpdatedAt,
            })));

        group.MapGet("/{id:guid}", async (HttpContext http, Guid id, ApplicationService applications, CancellationToken ct) =>
        {
            var d = await applications.GetAsync(http.User.UserId(), id, ct);
            return Results.Ok(new
            {
                d.Application.Id,
                d.Application.Status,
                d.Application.CoverLetter,
                d.Application.CreatedAt,
                d.Application.UpdatedAt,
                Offer = Summary(d.Offer),
            });
        });

        group.MapPut("/{id:guid}/letter", async (HttpContext http, Guid id, LetterRequest request, ApplicationService applications, CancellationToken ct) =>
        {
            await applications.EditLetterAsync(http.User.UserId(), id, request.CoverLetter, ct);
            return Results.NoContent();
        });

        group.MapGet("/{id:guid}/cv.pdf", async (HttpContext http, Guid id, ApplicationService applications, CancellationToken ct) =>
            Results.File(await applications.RenderPdfAsync(http.User.UserId(), id, ct), "application/pdf"))
            .RequireRateLimiting(CvEndpoints.HeavyPolicy);

        Act("confirm", ApplicationAction.Confirm);
        Act("sent", ApplicationAction.MarkSent);
        Act("discard", ApplicationAction.Discard);
        Act("reopen", ApplicationAction.Reopen);

        void Act(string name, ApplicationAction action) =>
            group.MapPost($"/{{id:guid}}/{name}", async (HttpContext http, Guid id, ApplicationService applications, CancellationToken ct) =>
                Results.Ok(new { Status = await applications.ActAsync(http.User.UserId(), id, action, ct) }));
    }

    private static object Summary(JobOffer o) => new { o.Id, o.Title, o.Company, o.Location, o.Source, o.Url };
}
