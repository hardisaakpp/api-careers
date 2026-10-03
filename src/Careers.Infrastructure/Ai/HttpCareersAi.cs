using System.Net.Http.Json;
using System.Text.Json;
using Careers.Application;
using Careers.Domain.Cv;
using Careers.Domain.Offers;
using Microsoft.Extensions.Logging;

namespace Careers.Infrastructure.Ai;

public sealed class AiOptions
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:8930/";

    /// <summary>Shared secret with careers-ai (sent as <c>X-Api-Key</c>).</summary>
    public string ApiKey { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 180;
}

public sealed class HttpCareersAi(HttpClient http, ILogger<HttpCareersAi> logger) : ICareersAi
{
    public async Task<RankResult> RankAsync(CvDocument cv, SearchPreferences preferences, IReadOnlyList<JobOffer> offers, CancellationToken ct)
    {
        var response = await PostAsync<AiRankResponse>("v1/rank",
            new AiRankRequest(AiMapper.ToAi(cv), AiMapper.ToAi(preferences), offers.Select(AiMapper.ToAi).ToList()), ct);
        var ranked = response.Results
            .Where(r => Guid.TryParse(r.Id, out _))
            .Select(r => new RankedOffer(Guid.Parse(r.Id), r.Score, r.Excluded, r.Matches))
            .ToList();
        return new RankResult(response.Pending, ranked);
    }

    public async Task<CvDocument> AdaptAsync(CvDocument cv, JobOffer offer, CancellationToken ct) =>
        AiMapper.FromAi((await PostAsync<AiAdaptResponse>("v1/adapt", new AiOfferRequest(AiMapper.ToAi(cv), AiMapper.ToAi(offer)), ct)).Cv);

    public async Task<string> LetterAsync(CvDocument cv, JobOffer offer, CancellationToken ct) =>
        (await PostAsync<AiLetterResponse>("v1/letter", new AiOfferRequest(AiMapper.ToAi(cv), AiMapper.ToAi(offer)), ct)).Letter;

    public async Task<byte[]> RenderPdfAsync(CvDocument cv, CancellationToken ct)
    {
        using var response = await SendAsync("v1/render", new AiRenderRequest(AiMapper.ToAi(cv)), ct);
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private async Task<T> PostAsync<T>(string path, object body, CancellationToken ct)
    {
        using var response = await SendAsync(path, body, ct);
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(ct) ?? throw new AiUnavailableException("The AI service sent an empty answer.");
        }
        catch (JsonException e)
        {
            throw new AiUnavailableException("The AI service sent an unreadable answer.", e);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(string path, object body, CancellationToken ct)
    {
        try
        {
            var response = await http.PostAsJsonAsync(path, body, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("careers-ai {Path} answered {Status}", path, (int)response.StatusCode);
                response.Dispose();
                throw new AiUnavailableException($"The AI service answered {(int)response.StatusCode}.");
            }

            return response;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new AiUnavailableException("The AI service is unreachable.", e);
        }
    }
}
