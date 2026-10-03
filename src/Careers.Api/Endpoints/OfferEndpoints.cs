using Careers.Api.Auth;
using Careers.Application.Offers;

namespace Careers.Api.Endpoints;

public static class OfferEndpoints
{
    public static void MapOfferEndpoints(this IEndpointRouteBuilder app)
    {
        // Offers ordered by closeness to the signed-in person's CV, each with what matched and where to apply.
        app.MapGet("/offers", async (HttpContext http, RankingService ranking, int? limit, CancellationToken ct) =>
        {
            var result = await ranking.RankAsync(http.User.UserId(), limit ?? 30, ct);
            return Results.Ok(new
            {
                result.Total,
                result.Excluded,
                result.Pending,
                Offers = result.Offers.Select(i => new
                {
                    i.Offer.Id,
                    i.Offer.Title,
                    i.Offer.Company,
                    i.Offer.Location,
                    i.Offer.Source,
                    AlsoOn = i.Offer.AlsoOn.Split(',', StringSplitOptions.RemoveEmptyEntries),
                    i.Offer.Url,
                    i.Offer.PublishedAt,
                    i.Score,
                    i.Matches,
                    i.ApplicationId,
                    i.ApplicationStatus,
                }),
            });
        }).RequireRateLimiting(CvEndpoints.HeavyPolicy);

        // Asks the public sources for new offers. Each is consulted at most once every 6 hours unless forced.
        app.MapPost("/offers/refresh", async (OfferCaptureService capture, bool? force, CancellationToken ct) =>
            Results.Ok(new
            {
                Results = (await capture.RunAsync(force ?? false, ct)).Select(r => new
                {
                    r.Source,
                    Outcome = r.Outcome.ToString(),
                    r.Read,
                    r.Added,
                }),
            }));
    }
}
