using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AstroNucleaus.Wpf.ViewModels;

/// <summary>
/// Main window: sidebar navigation between the pages.
/// </summary>
public partial class ShellViewModel : ObservableObject
{
    public ShellViewModel(EndfBrowserViewModel browser, SProcessViewModel sProcess, SettingsViewModel settings)
    {
        Browser = browser;
        SProcess = sProcess;
        Settings = settings;
    }

    public SProcessViewModel SProcess { get; }

    public string Title => "AstroNucleaus — ENDF Browser";

    public string Version =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0]
        ?? "1.0.0";

    public EndfBrowserViewModel Browser { get; }

    public SettingsViewModel Settings { get; }

    [ObservableProperty]
    private AppPage _selectedPage = AppPage.Browser;

    partial void OnSelectedPageChanged(AppPage value)
    {
        if (value == AppPage.Settings)
            Settings.RefreshCacheSizeCommand.Execute(null);
    }

    [RelayCommand]
    private void ShowBrowser() => SelectedPage = AppPage.Browser;

    public void Initialize() => Browser.Initialize();
}
