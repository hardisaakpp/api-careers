using Careers.Application.Applications;
using Careers.Application.Cv;
using Careers.Application.Offers;
using Microsoft.Extensions.DependencyInjection;

namespace Careers.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new OfferCaptureOptions());
        services.AddScoped<CvService>();
        services.AddScoped<OfferCaptureService>();
        services.AddScoped<RankingService>();
        services.AddScoped<ApplicationService>();
        return services;
    }
}
