using AstroNucleaus.Application.Endf;
using Microsoft.Extensions.DependencyInjection;

namespace AstroNucleaus.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddTransient<EndfLibraryService>();
        return services;
    }
}
