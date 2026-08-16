using Blindify.Application.Answers;
using Blindify.Application.Qcm;
using Blindify.Application.Rounds;
using Blindify.Application.Scoring;
using Blindify.Application.Sessions;
using Microsoft.Extensions.DependencyInjection;

namespace Blindify.Application.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<IScoringService, ScoringService>();
        services.AddSingleton<IBonusScoringService, BonusScoringService>();
        services.AddSingleton<IAnswerMatcher, AnswerMatcher>();
        services.AddSingleton<IQcmGenerator, QcmGenerator>();
        services.AddSingleton<IRoundService, RoundService>();
        services.AddSingleton<IGameCodeGenerator, GameCodeGenerator>();
        services.AddSingleton<IGameSessionStore, GameSessionStore>();

        return services;
    }
}
