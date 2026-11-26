using AstroNucleaus.Application.Endf;
using AstroNucleaus.Application.SProcess;
using Microsoft.Extensions.DependencyInjection;

namespace AstroNucleaus.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddTransient<EndfLibraryService>();
        services.AddSingleton<SProcessService>();
        services.AddSingleton<RProcess.RProcessService>();
        return services;
    }
}
