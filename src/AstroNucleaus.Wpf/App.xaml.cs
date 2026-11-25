using System.Windows;
using AstroNucleaus.Application;
using AstroNucleaus.Infrastructure;
using AstroNucleaus.Wpf.Services;
using AstroNucleaus.Wpf.ViewModels;
using AstroNucleaus.Wpf.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AstroNucleaus.Wpf;

public partial class App : System.Windows.Application
{
    private readonly IHost _host;

    public App()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddApplication();
                services.AddInfrastructure();

                services.AddSingleton<IFileDialogService, FileDialogService>();
                services.AddSingleton<EndfBrowserViewModel>();
                services.AddSingleton<SProcessViewModel>();
                services.AddSingleton<SettingsViewModel>();
                services.AddSingleton<ShellViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        await _host.StartAsync();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await _host.StopAsync();
        _host.Dispose();

        base.OnExit(e);
    }
}
