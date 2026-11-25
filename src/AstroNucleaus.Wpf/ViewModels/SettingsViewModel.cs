using System.Diagnostics;
using System.IO;
using System.Windows;
using AstroNucleaus.Infrastructure.Endf;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;

namespace AstroNucleaus.Wpf.ViewModels;

/// <summary>
/// Shows the download settings and manages the local ENDF cache.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly EndfOptions _options;

    public SettingsViewModel(IOptions<EndfOptions> options)
    {
        _options = options.Value;
        NndcLibraries = _options.NndcLibraries
            .Select(l => new NndcLibraryRow(l.Name, l.ArchiveUrlTemplate, string.Join(", ", l.Sublibraries)))
            .ToList();
        RefreshCacheSize();
    }

    public string CacheDirectory => _options.CacheDirectory;

    public string UserAgent => _options.UserAgent;

    public string Timeout => $"{_options.Timeout.TotalMinutes:0} min";

    public string IaeaAddress => _options.IaeaBaseAddress.AbsoluteUri;

    public IReadOnlyList<NndcLibraryRow> NndcLibraries { get; }

    [ObservableProperty]
    private string _cacheSize = string.Empty;

    [RelayCommand]
    private void OpenCacheFolder()
    {
        Directory.CreateDirectory(CacheDirectory);
        Process.Start(new ProcessStartInfo(CacheDirectory) { UseShellExecute = true });
    }

    [RelayCommand]
    private void ClearCache()
    {
        var answer = MessageBox.Show(
            $"Delete all downloaded ENDF files ({CacheSize}) in\n{CacheDirectory}?\n\nThey will be downloaded again when needed.",
            "Clear download cache",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK)
            return;

        try
        {
            if (Directory.Exists(CacheDirectory))
                Directory.Delete(CacheDirectory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(ex.Message, "Clear download cache", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        RefreshCacheSize();
    }

    [RelayCommand]
    private void RefreshCacheSize()
    {
        if (!Directory.Exists(CacheDirectory))
        {
            CacheSize = "empty";
            return;
        }

        var files = Directory.EnumerateFiles(CacheDirectory, "*", SearchOption.AllDirectories).Select(f => new FileInfo(f)).ToList();
        var bytes = files.Sum(f => f.Length);
        CacheSize = $"{files.Count} files, {bytes / (1024.0 * 1024.0):F1} MB";
    }

    public sealed record NndcLibraryRow(string Name, string ArchiveUrl, string Sublibraries);
}
