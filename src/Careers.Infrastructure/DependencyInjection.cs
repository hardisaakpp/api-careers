using Careers.Application;
using Careers.Infrastructure.Ai;
using Careers.Infrastructure.Feeds;
using Careers.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Careers.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<CareersDbContext>(options => options.UseSqlServer(configuration.GetConnectionString("Default")));
        services.AddScoped<ICvRepository, EfCvRepository>();
        services.AddScoped<IPreferencesRepository, EfPreferencesRepository>();
        services.AddScoped<IOfferRepository, EfOfferRepository>();
        services.AddScoped<IApplicationRepository, EfApplicationRepository>();
        services.AddScoped<IFeedPollStore, EfFeedPollStore>();

        services.Configure<AiOptions>(configuration.GetSection("Ai"));
        services.AddHttpClient<ICareersAi, HttpCareersAi>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<AiOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl.EndsWith('/') ? options.BaseUrl : options.BaseUrl + "/");
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            client.DefaultRequestHeaders.Add("X-Api-Key", options.ApiKey);
        });

        // One named client per feed, so each keeps its own settings.
        void Feed<T>() where T : class, IOfferFeed
        {
            services.AddHttpClient(typeof(T).Name, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("api-careers/0.1 (personal use)");
            });
            services.AddTransient<IOfferFeed>(sp => ActivatorUtilities.CreateInstance<T>(
                sp, sp.GetRequiredService<IHttpClientFactory>().CreateClient(typeof(T).Name)));
        }

        Feed<RemotiveFeed>();
        Feed<RemoteOkFeed>();
        Feed<WeWorkRemotelyFeed>();
        return services;
    }
}
