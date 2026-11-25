using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using AstroNucleaus.Application.Endf;
using AstroNucleaus.Domain.Endf;
using AstroNucleaus.Wpf.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AstroNucleaus.Wpf.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private static readonly string[] PreferredLibraries = ["ENDF-B-VIII.1", "ENDF-B-VIII.0"];
    private static readonly string[] PreferredSublibraries = ["n", "neutrons"];

    private readonly EndfLibraryService _endf;
    private readonly IFileDialogService _fileDialogs;
    private CancellationTokenSource? _listCts;
    private CancellationTokenSource? _operationCts;

    public MainViewModel(EndfLibraryService endf, IFileDialogService fileDialogs)
    {
        _endf = endf;
        _fileDialogs = fileDialogs;

        FilesView = CollectionViewSource.GetDefaultView(Files);
        FilesView.Filter = item => item is EndfRemoteFile file && MatchesFilter(file);

        Sources = endf.Sources;
    }

    public string Title => "AstroNucleaus — ENDF browser";

    public IReadOnlyList<EndfSourceInfo> Sources { get; }

    public ObservableCollection<string> Libraries { get; } = [];

    public ObservableCollection<string> Sublibraries { get; } = [];

    public ObservableCollection<EndfRemoteFile> Files { get; } = [];

    public ICollectionView FilesView { get; }

    public ObservableCollection<EndfMaterial> Materials { get; } = [];

    public ObservableCollection<string> Warnings { get; } = [];

    [ObservableProperty]
    private EndfSourceInfo? _selectedSource;

    [ObservableProperty]
    private string? _selectedLibrary;

    [ObservableProperty]
    private string? _selectedSublibrary;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    private EndfRemoteFile? _selectedFile;

    [ObservableProperty]
    private EndfMaterial? _selectedMaterial;

    [ObservableProperty]
    private CrossSection? _selectedCrossSection;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenFileCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isLoadingList;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _isProgressIndeterminate;

    [ObservableProperty]
    private string _status = "Ready";

    [ObservableProperty]
    private string? _loadedSource;

    /// <summary>Selects the preferred source, which loads its libraries.</summary>
    public void Initialize() => SelectedSource ??= Sources.FirstOrDefault();

    [RelayCommand]
    private async Task LoadLibrariesAsync()
    {
        Libraries.Clear();
        Sublibraries.Clear();
        Files.Clear();
        if (SelectedSource is not { } source)
            return;

        var libraries = await LoadListAsync($"Loading libraries from {source.Name}…", ct => _endf.GetLibrariesAsync(source.Id, ct));
        if (libraries is null)
            return;

        Replace(Libraries, libraries);
        SelectedLibrary =
            PreferredLibraries.FirstOrDefault(libraries.Contains)
            ?? libraries.FirstOrDefault(l => l.StartsWith("ENDF-B", StringComparison.OrdinalIgnoreCase))
            ?? libraries.FirstOrDefault();
        Status = $"{source.Name}: {libraries.Count} libraries";
    }

    partial void OnSelectedSourceChanged(EndfSourceInfo? value) => _ = LoadLibrariesAsync();

    partial void OnSelectedLibraryChanged(string? value) => _ = LoadSublibrariesAsync(value);

    partial void OnSelectedSublibraryChanged(string? value) => _ = LoadFilesAsync(SelectedLibrary, value);

    partial void OnFilterTextChanged(string value) => FilesView.Refresh();

    partial void OnSelectedMaterialChanged(EndfMaterial? value) =>
        SelectedCrossSection = value?.GetCrossSection(ReactionTypes.Total) ?? value?.CrossSections.FirstOrDefault();

    private async Task LoadSublibrariesAsync(string? library)
    {
        Sublibraries.Clear();
        Files.Clear();
        if (library is null || SelectedSource is not { } source)
            return;

        var sublibraries = await LoadListAsync($"Loading {library}…", ct => _endf.GetSublibrariesAsync(source.Id, library, ct));
        if (sublibraries is null)
            return;

        Replace(Sublibraries, sublibraries);
        SelectedSublibrary = PreferredSublibraries.FirstOrDefault(sublibraries.Contains) ?? sublibraries.FirstOrDefault();
        if (sublibraries.Count == 0)
            Status = $"{library} has no sub-libraries";
    }

    private async Task LoadFilesAsync(string? library, string? sublibrary)
    {
        Files.Clear();
        if (library is null || sublibrary is null || SelectedSource is not { } source)
            return;

        var files = await LoadListAsync(
            $"Loading {library}/{sublibrary}…", ct => _endf.GetFilesAsync(source.Id, library, sublibrary, ct));
        if (files is null)
            return;

        Replace(Files, files);
        Status = $"{library}/{sublibrary}: {files.Count} evaluations";
    }

    private bool CanDownload() => SelectedFile is not null && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task DownloadAsync()
    {
        var file = SelectedFile!;
        var progress = new Progress<DownloadProgress>(p =>
        {
            IsProgressIndeterminate = p.Fraction is null;
            Progress = (p.Fraction ?? 0) * 100;
            Status = $"Downloading {file.FileName}: {FormatSize(p.BytesReceived)}"
                     + (p.TotalBytes is { } total ? $" of {FormatSize(total)}" : string.Empty);
        });

        await RunOperationAsync($"Downloading {file.FileName}…", async ct =>
        {
            var tape = await _endf.DownloadAndParseAsync(file, progress, ct);
            ShowTape(tape, $"{file.Library}/{file.Sublibrary}/{file.FileName}");
        });
    }

    private bool CanOpenFile() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanOpenFile))]
    private async Task OpenFileAsync()
    {
        var path = _fileDialogs.OpenEndfFile();
        if (path is null)
            return;

        await RunOperationAsync($"Parsing {Path.GetFileName(path)}…", async ct =>
        {
            var tape = await _endf.ParseLocalFileAsync(path, ct);
            ShowTape(tape, path);
        });
    }

    private bool CanCancel() => IsBusy;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _operationCts?.Cancel();

    private void ShowTape(EndfTape tape, string source)
    {
        Replace(Materials, tape.Materials);
        Replace(Warnings, tape.Warnings);
        SelectedMaterial = Materials.FirstOrDefault();
        LoadedSource = source;

        var crossSections = tape.Materials.Sum(m => m.CrossSections.Count);
        Status = $"Loaded {source}: {tape.Materials.Count} material(s), {crossSections} cross section(s)"
                 + (tape.Warnings.Count > 0 ? $", {tape.Warnings.Count} warning(s)" : string.Empty);
    }

    private async Task RunOperationAsync(string startStatus, Func<CancellationToken, Task> operation)
    {
        _operationCts = new CancellationTokenSource();
        IsBusy = true;
        IsProgressIndeterminate = true;
        Progress = 0;
        Status = startStatus;

        try
        {
            await operation(_operationCts.Token);
        }
        catch (OperationCanceledException)
        {
            Status = "Cancelled";
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            IsProgressIndeterminate = false;
            Progress = 0;
            _operationCts.Dispose();
            _operationCts = null;
        }
    }

    /// <summary>Loads a list; a newer request cancels the previous one. Returns null on failure.</summary>
    private async Task<IReadOnlyList<T>?> LoadListAsync<T>(string status, Func<CancellationToken, Task<IReadOnlyList<T>>> load)
    {
        _listCts?.Cancel();
        var cts = new CancellationTokenSource();
        _listCts = cts;

        IsLoadingList = true;
        Status = status;
        try
        {
            return await load(cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
            return null;
        }
        finally
        {
            if (ReferenceEquals(_listCts, cts))
            {
                IsLoadingList = false;
                _listCts = null;
            }

            cts.Dispose();
        }
    }

    private bool MatchesFilter(EndfRemoteFile file)
    {
        var filter = FilterText.Trim();
        return filter.Length == 0
            || file.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || file.FileName.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private static void Replace<T>(ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items)
            collection.Add(item);
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):F1} MB",
        >= 1024 => $"{bytes / 1024.0:F0} KB",
        _ => $"{bytes} B",
    };
}
