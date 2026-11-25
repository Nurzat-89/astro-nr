using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using AstroNucleaus.Application.Endf;
using AstroNucleaus.Domain.Endf;
using AstroNucleaus.Wpf.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AstroNucleaus.Wpf.ViewModels;

/// <summary>
/// Browses ENDF sources, downloads evaluations, and shows the parsed data.
/// </summary>
public partial class EndfBrowserViewModel : ObservableObject
{
    private static readonly string[] PreferredLibraries = ["ENDF-B-VIII.1", "ENDF-B-VIII.0"];
    private static readonly string[] PreferredSublibraries = ["n", "neutrons"];

    private readonly EndfLibraryService _endf;
    private readonly IFileDialogService _fileDialogs;
    private readonly List<EndfRemoteFile> _files = [];
    private CancellationTokenSource? _listCts;
    private CancellationTokenSource? _operationCts;

    public EndfBrowserViewModel(EndfLibraryService endf, IFileDialogService fileDialogs)
    {
        _endf = endf;
        _fileDialogs = fileDialogs;
        Sources = endf.Sources;
    }

    public IReadOnlyList<EndfSourceInfo> Sources { get; }

    public ObservableCollection<string> Libraries { get; } = [];

    public ObservableCollection<string> Sublibraries { get; } = [];

    /// <summary>Remote evaluations grouped by element, after filtering.</summary>
    public ObservableCollection<ElementGroupViewModel> ElementGroups { get; } = [];

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
    [NotifyPropertyChangedFor(nameof(FileCountText))]
    private int _fileCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    private EndfRemoteFile? _selectedFile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMaterial))]
    [NotifyPropertyChangedFor(nameof(MaterialTitle))]
    [NotifyPropertyChangedFor(nameof(MaterialSubtitle))]
    [NotifyPropertyChangedFor(nameof(BadgeSymbol))]
    [NotifyPropertyChangedFor(nameof(BadgeMassNumber))]
    [NotifyPropertyChangedFor(nameof(LocationText))]
    private EndfMaterial? _selectedMaterial;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlotTitle))]
    [NotifyPropertyChangedFor(nameof(ThresholdText))]
    [NotifyPropertyChangedFor(nameof(PointCountText))]
    [NotifyCanExecuteChangedFor(nameof(ExportCsvCommand))]
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
    private bool _hasError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MaterialSubtitle))]
    [NotifyPropertyChangedFor(nameof(LocationText))]
    private string? _loadedLibraryText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LocationText))]
    private string? _loadedLocation;

    [ObservableProperty]
    private string _warningsHeader = "Warnings";

    [ObservableProperty]
    private bool _plotShowGrid = true;

    [ObservableProperty]
    private bool _plotShowMarkers;

    [ObservableProperty]
    private bool _plotShowReadout = true;

    public bool HasMaterial => SelectedMaterial is not null;

    public string FileCountText => FileCount == 1 ? "1 evaluation" : $"{FileCount} evaluations";

    public string MaterialTitle => SelectedMaterial?.Name ?? "No material loaded";

    public string MaterialSubtitle => SelectedMaterial is { } m
        ? $"MAT {m.Mat}" + (LoadedLibraryText is { Length: > 0 } library ? $"  ·  {library}" : string.Empty)
        : "Pick an evaluation on the left and download it, or open a local ENDF file.";

    public string BadgeSymbol => SelectedMaterial?.Symbol ?? "?";

    public string BadgeMassNumber => SelectedMaterial is { MassNumber: > 0 } m
        ? m.MassNumber.ToString(CultureInfo.InvariantCulture)
        : string.Empty;

    public string PlotTitle => SelectedMaterial is { } m && SelectedCrossSection is { } xs
        ? $"{m.Name} {xs.ReactionName}"
        : "Cross section";

    public string ThresholdText => SelectedCrossSection is { } xs
        ? $"threshold: {xs.ThresholdEnergy.ToString("0.###E+00", CultureInfo.InvariantCulture)} eV"
        : string.Empty;

    public string PointCountText => SelectedCrossSection is { } xs
        ? $"{xs.Data.Count.ToString("N0", CultureInfo.CurrentCulture)} points"
        : string.Empty;

    public string LocationText => SelectedMaterial is { } m
        ? $"{LoadedLocation} / {m}"
        : LoadedLocation ?? "No file loaded";

    /// <summary>Selects the preferred source, which loads its libraries.</summary>
    public void Initialize() => SelectedSource ??= Sources.FirstOrDefault();

    partial void OnStatusChanged(string value) =>
        HasError = value.StartsWith("Error", StringComparison.Ordinal);

    partial void OnSelectedSourceChanged(EndfSourceInfo? value) => _ = LoadLibrariesAsync();

    partial void OnSelectedLibraryChanged(string? value) => _ = LoadSublibrariesAsync(value);

    partial void OnSelectedSublibraryChanged(string? value) => _ = LoadFilesAsync(SelectedLibrary, value);

    partial void OnFilterTextChanged(string value) => RebuildGroups();

    partial void OnSelectedMaterialChanged(EndfMaterial? value) =>
        SelectedCrossSection = value?.GetCrossSection(ReactionTypes.Total) ?? value?.CrossSections.FirstOrDefault();

    [RelayCommand]
    private Task RefreshAsync() => LoadLibrariesAsync();

    private async Task LoadLibrariesAsync()
    {
        Libraries.Clear();
        Sublibraries.Clear();
        SetFiles([]);
        if (SelectedSource is not { } source)
            return;

        var libraries = await LoadListAsync($"Loading libraries from {source.Name}…", ct => _endf.GetLibrariesAsync(source.Id, ct));
        if (libraries is null)
            return;

        Replace(Libraries, libraries);
        SelectedLibrary = null;
        SelectedLibrary =
            PreferredLibraries.FirstOrDefault(libraries.Contains)
            ?? libraries.FirstOrDefault(l => l.StartsWith("ENDF-B", StringComparison.OrdinalIgnoreCase))
            ?? libraries.FirstOrDefault();
    }

    private async Task LoadSublibrariesAsync(string? library)
    {
        Sublibraries.Clear();
        SetFiles([]);
        if (library is null || SelectedSource is not { } source)
            return;

        var sublibraries = await LoadListAsync($"Loading {library}…", ct => _endf.GetSublibrariesAsync(source.Id, library, ct));
        if (sublibraries is null)
            return;

        Replace(Sublibraries, sublibraries);
        SelectedSublibrary = null;
        SelectedSublibrary = PreferredSublibraries.FirstOrDefault(sublibraries.Contains) ?? sublibraries.FirstOrDefault();
    }

    private async Task LoadFilesAsync(string? library, string? sublibrary)
    {
        SetFiles([]);
        if (library is null || sublibrary is null || SelectedSource is not { } source)
            return;

        var files = await LoadListAsync(
            $"Loading {library} / {sublibrary}…", ct => _endf.GetFilesAsync(source.Id, library, sublibrary, ct));
        if (files is not null)
            SetFiles(files);
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
            Status = $"Downloading {file.DisplayName}: {FormatSize(p.BytesReceived)}"
                     + (p.TotalBytes is { } total ? $" of {FormatSize(total)}" : string.Empty);
        });

        await RunOperationAsync($"Downloading {file.DisplayName}…", async ct =>
        {
            var tape = await _endf.DownloadAndParseAsync(file, progress, ct);
            ShowTape(tape, $"{file.Library} ({file.Sublibrary})", $"{file.Library} / {file.Sublibrary}");
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
            ShowTape(tape, Path.GetFileName(path), path);
        });
    }

    private bool CanCancel() => IsBusy;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _operationCts?.Cancel();

    private bool CanExportCsv() => SelectedCrossSection is not null;

    [RelayCommand(CanExecute = nameof(CanExportCsv))]
    private void ExportCsv()
    {
        if (SelectedCrossSection is not { } xs)
            return;

        var defaultName = $"{SelectedMaterial?.Name ?? "material"}_MT{xs.MT}.csv";
        var path = _fileDialogs.SaveFile("Export cross section", "CSV files (*.csv)|*.csv", defaultName);
        if (path is null)
            return;

        try
        {
            var csv = new StringBuilder("energy_eV,cross_section_b\n");
            for (var i = 0; i < xs.Data.Count; i++)
            {
                csv.Append(xs.Data.X[i].ToString("R", CultureInfo.InvariantCulture)).Append(',')
                   .Append(xs.Data.Y[i].ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            }

            File.WriteAllText(path, csv.ToString());
            Status = $"Exported {xs.Data.Count} points to {Path.GetFileName(path)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Error: {ex.Message}";
        }
    }

    public string? AskImagePath()
    {
        var name = SelectedMaterial is { } m && SelectedCrossSection is { } xs ? $"{m.Name}_MT{xs.MT}.png" : "cross-section.png";
        return _fileDialogs.SaveFile("Save plot image", "PNG images (*.png)|*.png", name);
    }

    private void ShowTape(EndfTape tape, string libraryText, string location)
    {
        Replace(Materials, tape.Materials);
        Replace(Warnings, tape.Warnings);
        WarningsHeader = tape.Warnings.Count > 0 ? $"Warnings ({tape.Warnings.Count})" : "Warnings";
        LoadedLibraryText = libraryText;
        LoadedLocation = location;
        SelectedMaterial = null;
        SelectedMaterial = Materials.FirstOrDefault();
        Status = tape.Materials.Count == 0 ? "Error: the file contains no ENDF materials" : "Ready";
    }

    private void SetFiles(IReadOnlyList<EndfRemoteFile> files)
    {
        _files.Clear();
        _files.AddRange(files);
        RebuildGroups();
    }

    private void RebuildGroups()
    {
        var filter = FilterText.Trim();
        var matches = _files.Where(f =>
                filter.Length == 0
                || f.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || f.FileName.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var groups = matches
            .GroupBy(f => f.Symbol ?? "Other")
            .Select(g => new ElementGroupViewModel(g.Key, g.First().Z, g.ToList()))
            .OrderBy(g => g.Z ?? int.MaxValue)
            .ThenBy(g => g.Symbol, StringComparer.Ordinal)
            .ToList();

        // Expand everything while filtering, otherwise only the first element.
        for (var i = 0; i < groups.Count; i++)
            groups[i].IsExpanded = filter.Length > 0 || i == 0;

        Replace(ElementGroups, groups);
        FileCount = matches.Count;
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
            var result = await load(cts.Token);
            Status = "Ready";
            return result;
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
