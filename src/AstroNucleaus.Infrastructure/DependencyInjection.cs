using AstroNucleaus.Application.Abstractions;
using AstroNucleaus.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AstroNucleaus.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        return services;
    }
}
