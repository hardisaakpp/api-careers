using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Careers.Api;
using Careers.Api.Auth;
using Careers.Api.Background;
using Careers.Api.Endpoints;
using Careers.Application;
using Careers.Infrastructure;
using Careers.Infrastructure.Persistence;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.RespectNullableAnnotations = true;
    options.SerializerOptions.RespectRequiredConstructorParameters = true;
});
// PDFs and rankings call the AI service, which is slow and expensive: only a few at a time.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddConcurrencyLimiter(CvEndpoints.HeavyPolicy, limiter =>
    {
        limiter.PermitLimit = builder.Configuration.GetValue("RateLimiting:HeavyConcurrency", 4);
        limiter.QueueLimit = builder.Configuration.GetValue("RateLimiting:HeavyQueue", 20);
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
});
builder.Services.AddExceptionHandler<CareersExceptionHandler>();
builder.Services.AddProblemDetails();
if (builder.Configuration.GetValue("OfferFeeds:AutoRefresh", true))
{
    builder.Services.AddHostedService<OfferFeedWorker>();
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<CareersDbContext>().Database.Migrate();
}

app.UseExceptionHandler();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTimeOffset.UtcNow })).AllowAnonymous();
app.MapCvEndpoints();
app.MapOfferEndpoints();
app.MapApplicationEndpoints();

app.Run();

public partial class Program;
