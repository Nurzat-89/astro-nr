using AstroNucleaus.Application.Abstractions;
using AstroNucleaus.Application.Endf;
using AstroNucleaus.Application.SProcess;
using AstroNucleaus.Infrastructure.Endf;
using AstroNucleaus.Infrastructure.Endf.Download;
using AstroNucleaus.Infrastructure.Endf.Parsing;
using AstroNucleaus.Infrastructure.Services;
using AstroNucleaus.Infrastructure.SProcess;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AstroNucleaus.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        Action<EndfOptions>? configureEndf = null)
    {
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

        var endfOptions = services.AddOptions<EndfOptions>();
        if (configureEndf is not null)
            endfOptions.Configure(configureEndf);

        services.AddSingleton<IEndfParser, EndfParser>();

        services.AddHttpClient<NndcEndfLibraryCatalog>(ConfigureEndfClient);
        services.AddHttpClient<IaeaEndfLibraryCatalog>((sp, client) =>
        {
            ConfigureEndfClient(sp, client);
            var baseAddress = sp.GetRequiredService<IOptions<EndfOptions>>().Value.IaeaBaseAddress.AbsoluteUri;
            client.BaseAddress = new Uri(baseAddress.EndsWith('/') ? baseAddress : baseAddress + "/");
        });
        services.AddHttpClient<IEndfFileDownloader, HttpEndfFileDownloader>(ConfigureEndfClient);
        services.AddHttpClient<ISProcessDataSource, NndcSProcessDataSource>(ConfigureEndfClient);

        // Sources in order of preference: NNDC first, the IAEA server refuses non-browser clients.
        services.AddTransient<IEndfLibraryCatalog>(sp => sp.GetRequiredService<NndcEndfLibraryCatalog>());
        services.AddTransient<IEndfLibraryCatalog>(sp => sp.GetRequiredService<IaeaEndfLibraryCatalog>());

        return services;
    }

    private static void ConfigureEndfClient(IServiceProvider services, HttpClient client)
    {
        var options = services.GetRequiredService<IOptions<EndfOptions>>().Value;
        client.Timeout = options.Timeout;
        client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
    }
}
