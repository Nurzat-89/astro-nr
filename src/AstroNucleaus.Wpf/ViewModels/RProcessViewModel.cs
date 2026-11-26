using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using AstroNucleaus.Application.RProcess;
using AstroNucleaus.Application.SProcess;
using AstroNucleaus.Domain.RProcess;
using AstroNucleaus.Wpf.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AstroNucleaus.Wpf.ViewModels;

/// <summary>
/// Inputs, data files, run and results of the r-process network.
/// </summary>
public partial class RProcessViewModel : ObservableObject
{
    private const double KeVPerGK = 86.17333262;

    private readonly RProcessService _service;
    private readonly IFileDialogService _fileDialogs;
    private CancellationTokenSource? _cts;

    public RProcessViewModel(RProcessService service, IFileDialogService fileDialogs)
    {
        _service = service;
        _fileDialogs = fileDialogs;
        RefreshStatus();
    }

    public IReadOnlyList<int> MaxZOptions { get; } = [50, 60, 70, 83, 92, 100];

    public ObservableCollection<RProcessAbundance> TableRows { get; } = [];

    public ObservableCollection<string> Warnings { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KTText))]
    private double _temperatureGK = 1.2;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeutronDensityText))]
    private double _log10NeutronDensity = 24;

    [ObservableProperty]
    private double _durationSeconds = 1.5;

    [ObservableProperty]
    private int _maxZ = 92;

    [ObservableProperty]
    private double _decayYears = 1e9;

    [ObservableProperty]
    private bool _significantOnly = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private bool _hasReaclib;

    [ObservableProperty]
    private string _reaclibText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveMassTableCommand))]
    private bool _hasMassTable;

    [ObservableProperty]
    private string _massTableText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    [NotifyPropertyChangedFor(nameof(NeutronsPerSeedText))]
    [NotifyPropertyChangedFor(nameof(MeanAText))]
    [NotifyPropertyChangedFor(nameof(LostText))]
    [NotifyPropertyChangedFor(nameof(WaitingPointsText))]
    [NotifyPropertyChangedFor(nameof(ConditionsText))]
    [NotifyPropertyChangedFor(nameof(DataText))]
    [NotifyPropertyChangedFor(nameof(Abundances))]
    [NotifyPropertyChangedFor(nameof(Chains))]
    [NotifyCanExecuteChangedFor(nameof(ExportCsvCommand))]
    private RProcessResult? _result;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportReaclibCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadReaclibCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportMassTableCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _isProgressIndeterminate;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    public bool HasResult => Result is not null;

    public IReadOnlyList<RProcessAbundance>? Abundances => Result?.Abundances;

    /// <summary>Animated chart of nuclides for the Path tab: irradiation, then decay after freeze-out.</summary>
    public NuclidePlaybackViewModel Playback { get; } = new();

    partial void OnResultChanged(RProcessResult? value) => Playback.Load(value is null ? null : ToPlayback(value));

    private static PlaybackData ToPlayback(RProcessResult r)
    {
        var nuclides = r.Abundances
            .Select(a => new PlaybackNuclide(
                a.Nuclide,
                double.IsPositiveInfinity(a.BetaHalfLifeSeconds),
                (double.IsPositiveInfinity(a.BetaHalfLifeSeconds) ? "no β decay" : $"T½(β) = {PlaybackFormatting.Seconds(a.BetaHalfLifeSeconds)}")
                + "\n" + (a.NeutronSeparationMeV is { } sn ? $"S_n = {sn:0.000} MeV" : "S_n: no mass")))
            .ToList();

        var frames = r.FrameInfos;
        string Describe(int k, double f)
        {
            var i0 = frames[k];
            var i1 = frames[k + 1];
            var phase = i1.IsIrradiation
                ? $"Neutron irradiation (n_n = {r.Parameters.NeutronDensity:0.0E+0} cm⁻³, T = {r.Parameters.TemperatureGK:0.##} GK)"
                : "β decay after freeze-out";
            return $"{phase} · ⟨Z⟩ = {i0.MeanZ + (i1.MeanZ - i0.MeanZ) * f:0.0} · ⟨A⟩ = {i0.MeanA + (i1.MeanA - i0.MeanA) * f:0.0}";
        }

        var freezeOut = Math.Max(0, frames.ToList().FindLastIndex(fi => fi.IsIrradiation));
        return new PlaybackData(
            "Abundances on the chart of nuclides: irradiation, then decay",
            "Y/Y₀(seed)",
            nuclides,
            frames.Select(fi => fi.TimeSeconds).ToList(),
            r.FrameAbundances,
            null,
            Describe,
            PlaybackFormatting.Seconds,
            freezeOut)
        {
            PhaseBoundary = freezeOut,
            PhasesName = "Irradiation | decay",
            DefaultPace = PlaybackPace.Phases,
        };
    }

    public IReadOnlyList<MassChainAbundance>? Chains => Result?.MassChains;

    public string KTText => $"kT = {TemperatureGK * KeVPerGK:0} keV";

    public string NeutronDensityText => $"n_n = {Math.Pow(10, Log10NeutronDensity):0.0E+0} cm⁻³";

    public string NeutronsPerSeedText => Result is { } r ? $"{r.NeutronsCapturedPerSeed:0.0}" : "–";

    public string MeanAText => Result is { } r ? $"{r.MeanAAtFreezeOut:0.0} → {r.MeanADecayed:0.0}" : "–";

    public string LostText => Result is { } r ? $"{r.LostBeyondMaxZ:P1}" : "–";

    public string WaitingPointsText => Result is { } r
        ? string.Join(", ", r.WaitingPoints.Where(n => n.N is 50 or 82 or 126).Select(n => n.ToString())) is { Length: > 0 } magic
            ? $"at magic N: {magic}"
            : $"{r.WaitingPoints.Count} elements populated"
        : string.Empty;

    public string ConditionsText => Result is { } r
        ? $"T = {r.Parameters.TemperatureGK:0.##} GK (kT = {r.KTkeV:0} keV) · n_n = {r.Parameters.NeutronDensity:0.0E+0} cm⁻³ · " +
          $"{r.Parameters.DurationSeconds:0.##} s, then {r.Parameters.DecayYears:0.##E+0} yr of decay · {r.NetworkSize} nuclides · " +
          $"(γ,n) from masses: {r.PhotodisintegrationFromMasses}, from REACLIB: {r.PhotodisintegrationFromReaclib}"
        : string.Empty;

    public string DataText => Result?.DataDescription ?? string.Empty;

    partial void OnStatusChanged(string value) => HasError = value.StartsWith("Error", StringComparison.Ordinal);

    partial void OnSignificantOnlyChanged(bool value) => RebuildTable();

    private void RefreshStatus()
    {
        var status = _service.GetStatus();
        HasReaclib = status.HasReaclib;
        ReaclibText = status.ReaclibFile is { } file
            ? $"{file}{(status.ReaclibImported is { } when ? $", imported {when:g}" : string.Empty)}"
            : "No REACLIB file yet — download it or load your own.";
        HasMassTable = status.MassTableFile is not null;
        MassTableText = status.MassTableFile is { } table
            ? $"AME2020 + {table} ({status.MassTableEntries} nuclides); REACLIB reverse rates where both lack a mass."
            : "AME2020; beyond it (γ,n) comes from the REACLIB reverse rates.";
        if (string.IsNullOrEmpty(Status) || Status.StartsWith("Load a REACLIB", StringComparison.Ordinal))
            Status = HasReaclib ? "Set T, n_n and duration, then run." : "Load a REACLIB file to start.";
    }

    private bool CanImport() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task DownloadReaclibAsync()
    {
        await RunBusyAsync("Downloading REACLIB…", async ct =>
        {
            var progress = new Progress<SProcessProgress>(p =>
            {
                Status = p.Stage;
                IsProgressIndeterminate = p.Fraction is null;
                Progress = (p.Fraction ?? 0) * 100;
            });
            await _service.DownloadReaclibAsync(progress, ct);
            RefreshStatus();
            Status = "REACLIB downloaded and checked. Set T, n_n and duration, then run.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportReaclibAsync()
    {
        var path = _fileDialogs.OpenFile("Open REACLIB file", "REACLIB files (*.*)|*.*");
        if (path is null)
            return;

        await RunBusyAsync("Checking the REACLIB file…", async ct =>
        {
            await _service.ImportReaclibAsync(path, ct);
            RefreshStatus();
            Status = $"REACLIB file {Path.GetFileName(path)} imported.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportMassTableAsync()
    {
        var path = _fileDialogs.OpenFile("Open mass table (Z A mass-excess in MeV)", "Text files (*.txt;*.dat;*.csv)|*.txt;*.dat;*.csv|All files (*.*)|*.*");
        if (path is null)
            return;

        await RunBusyAsync("Checking the mass table…", async ct =>
        {
            await _service.ImportMassTableAsync(path, ct);
            RefreshStatus();
            Status = $"Mass table {Path.GetFileName(path)} imported.";
        });
    }

    private bool CanRemoveMassTable() => HasMassTable && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRemoveMassTable))]
    private void RemoveMassTable()
    {
        _service.RemoveMassTable();
        RefreshStatus();
        Status = "Theoretical mass table removed; AME2020 only.";
    }

    private bool CanRun() => !IsBusy && HasReaclib;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        var parameters = new RProcessParameters
        {
            TemperatureGK = TemperatureGK,
            NeutronDensity = Math.Pow(10, Log10NeutronDensity),
            DurationSeconds = DurationSeconds,
            MaxZ = MaxZ,
            DecayYears = DecayYears,
        };

        var errors = parameters.Validate();
        if (errors.Count > 0)
        {
            Status = "Error: " + string.Join(" ", errors);
            return;
        }

        await RunBusyAsync("Starting…", async ct =>
        {
            var stopwatch = Stopwatch.StartNew();
            var progress = new Progress<SProcessProgress>(p =>
            {
                Status = p.Stage;
                IsProgressIndeterminate = p.Fraction is null;
                Progress = (p.Fraction ?? 0) * 100;
            });

            Result = await _service.RunAsync(parameters, progress, ct);
            RebuildTable();
            Warnings.Clear();
            foreach (var warning in Result.Warnings)
                Warnings.Add(warning);
            Status = $"Done in {stopwatch.Elapsed.TotalSeconds:0.0} s: {Result.NeutronsCapturedPerSeed:0.0} neutrons per seed, " +
                     $"⟨A⟩ = {Result.MeanADecayed:0.0} after decay";
        });
    }

    private bool CanCancel() => IsBusy;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _cts?.Cancel();

    private bool CanExport() => Result is not null;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private void ExportCsv()
    {
        if (Result is not { } r)
            return;

        var c = CultureInfo.InvariantCulture;
        var path = _fileDialogs.SaveFile("Export r-process abundances", "CSV files (*.csv)|*.csv",
            string.Create(c, $"rprocess_T{r.Parameters.TemperatureGK:0.##}_nn{Math.Log10(r.Parameters.NeutronDensity):0.#}_t{r.Parameters.DurationSeconds:0.##}s.csv"));
        if (path is null)
            return;

        var csv = new StringBuilder();
        csv.AppendLine(c, $"# r-process network: T = {r.Parameters.TemperatureGK} GK, n_n = {r.Parameters.NeutronDensity:R} cm-3, duration = {r.Parameters.DurationSeconds} s, decay = {r.Parameters.DecayYears} yr");
        csv.AppendLine("Z,N,A,nuclide,Y_freezeout,Y_decayed,beta_half_life_s,Sn_MeV");
        foreach (var a in r.Abundances)
        {
            csv.AppendLine(c,
                $"{a.Nuclide.Z},{a.Nuclide.N},{a.Nuclide.A},{a.Nuclide},{a.AtFreezeOut:R},{a.Decayed:R}," +
                $"{(double.IsPositiveInfinity(a.BetaHalfLifeSeconds) ? "stable" : a.BetaHalfLifeSeconds.ToString("R", c))},{a.NeutronSeparationMeV?.ToString("R", c)}");
        }

        try
        {
            File.WriteAllText(path, csv.ToString());
            Status = $"Exported {r.Abundances.Count} nuclides to {Path.GetFileName(path)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Error: {ex.Message}";
        }
    }

    private async Task RunBusyAsync(string startStatus, Func<CancellationToken, Task> action)
    {
        _cts = new CancellationTokenSource();
        IsBusy = true;
        IsProgressIndeterminate = true;
        Progress = 0;
        Status = startStatus;
        try
        {
            await action(_cts.Token);
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
            _cts.Dispose();
            _cts = null;
        }
    }

    private void RebuildTable()
    {
        TableRows.Clear();
        if (Result is null)
            return;

        foreach (var a in Result.Abundances
                     .Where(a => !SignificantOnly || a.AtFreezeOut > 1e-6 || a.Decayed > 1e-6)
                     .OrderBy(a => a.Nuclide.Z).ThenBy(a => a.Nuclide.A))
        {
            TableRows.Add(a);
        }
    }
}
