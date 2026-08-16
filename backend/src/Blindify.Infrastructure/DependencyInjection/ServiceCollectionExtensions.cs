using Blindify.Infrastructure.Configuration;
using Blindify.Infrastructure.Stats;
using Blindify.Infrastructure.Tracks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Blindify.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DataPathsOptions>(configuration.GetSection(DataPathsOptions.SectionName));
        services.AddSingleton<ITracksRepository, TracksRepository>();
        services.AddSingleton<IStatsRepository, StatsRepository>();

        return services;
    }
}
