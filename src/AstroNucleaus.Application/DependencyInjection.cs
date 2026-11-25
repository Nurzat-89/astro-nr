using Microsoft.Extensions.DependencyInjection;

namespace AstroNucleaus.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Register application services / use cases here.
        return services;
    }
}
