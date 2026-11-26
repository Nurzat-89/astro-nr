using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using AstroNucleaus.Application.SProcess;
using AstroNucleaus.Domain.SProcess;
using AstroNucleaus.Wpf.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AstroNucleaus.Wpf.ViewModels;

/// <summary>
/// Inputs, run and results of the weak s-process calculation.
/// </summary>
public partial class SProcessViewModel : ObservableObject
{
    private const double KeVPerGK = 86.17333262;

    private readonly SProcessService _service;
    private readonly IFileDialogService _fileDialogs;
    private CancellationTokenSource? _cts;

    public SProcessViewModel(SProcessService service, IFileDialogService fileDialogs)
    {
        _service = service;
        _fileDialogs = fileDialogs;
    }

    public IReadOnlyList<int> MaxZOptions { get; } = [36, 38, 40, 42, 46, 50];

    /// <summary>Animated chart of nuclides for the Path tab.</summary>
    public NuclidePlaybackViewModel Playback { get; } = new();

    partial void OnResultChanged(SProcessResult? value) => Playback.Load(value is null ? null : ToPlayback(value));

    private static PlaybackData ToPlayback(SProcessResult r)
    {
        var nuclides = r.Abundances
            .Select(a => new PlaybackNuclide(
                a.Nuclide,
                a.IsStable,
                (a.IsStable ? "stable" : $"T½ = {PlaybackFormatting.Seconds(a.HalfLifeSeconds)}") + $"\nMACS = {a.CaptureBarns * 1000:0.###} mb"))
            .ToList();

        string Describe(int k, double f)
        {
            var h0 = r.History[k];
            var h1 = r.History[k + 1];
            double Lerp(double x, double y) => x + (y - x) * f;
            return $"τ = {Lerp(h0.Exposure, h1.Exposure):0.0000} mb⁻¹ · n_n = {PlaybackFormatting.NeutronDensity(Lerp(h0.NeutronDensity, h1.NeutronDensity))} cm⁻³ · " +
                   $"X(He) = {Lerp(h0.HeliumMassFraction, h1.HeliumMassFraction):0.000} · X(²²Ne) = {Lerp(h0.Ne22MassFraction, h1.Ne22MassFraction):0.0E+0}";
        }

        return new PlaybackData(
            "Isotope abundances on the chart of nuclides",
            "Y/Y₀(⁵⁶Fe)",
            nuclides,
            r.History.Select(h => h.TimeYears).ToList(),
            r.HistoryAbundances,
            r.History.Select(h => h.Exposure).ToList(),
            Describe,
            PlaybackFormatting.Years,
            r.History.Count - 1);
    }

    public ObservableCollection<NuclideAbundance> TableRows { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DensityPlaceholder))]
    [NotifyPropertyChangedFor(nameof(DurationPlaceholder))]
    private double _stellarMass = 25;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KTText))]
    private double _temperatureGK = 0.30;

    [ObservableProperty]
    private double _metallicity = 0.014;

    [ObservableProperty]
    private double _heliumAtOnset = 0.1;

    [ObservableProperty]
    private double _carbonFraction = 0.3;

    [ObservableProperty]
    private string _densityText = string.Empty;

    [ObservableProperty]
    private string _durationText = string.Empty;

    [ObservableProperty]
    private int _maxZ = 42;

    [ObservableProperty]
    private bool _showDecayed = true;

    [ObservableProperty]
    private bool _stableOnly = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    [NotifyPropertyChangedFor(nameof(ExposureText))]
    [NotifyPropertyChangedFor(nameof(NeutronDensityText))]
    [NotifyPropertyChangedFor(nameof(PeakDensityText))]
    [NotifyPropertyChangedFor(nameof(Ne22Text))]
    [NotifyPropertyChangedFor(nameof(Fe56Text))]
    [NotifyPropertyChangedFor(nameof(ConditionsText))]
    [NotifyPropertyChangedFor(nameof(DataText))]
    [NotifyPropertyChangedFor(nameof(AllNe22Burned))]
    [NotifyPropertyChangedFor(nameof(Abundances))]
    [NotifyCanExecuteChangedFor(nameof(ExportCsvCommand))]
    private SProcessResult? _result;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _isProgressIndeterminate;

    [ObservableProperty]
    private string _status = "Set the star's mass and temperature, then run.";

    [ObservableProperty]
    private bool _hasError;

    public bool HasResult => Result is not null;

    public IReadOnlyList<NuclideAbundance>? Abundances => Result?.Abundances;

    public string KTText => $"kT = {TemperatureGK * KeVPerGK:0.0} keV";

    public string DensityPlaceholder => $"auto: {StellarCoreModel.DefaultDensity(StellarMass):0} g/cm³";

    public string DurationPlaceholder => $"auto: {StellarCoreModel.DefaultHeliumBurningYears(StellarMass) / 1e6:0.00} Myr";

    public string ExposureText => Result is { } r ? $"{r.NeutronExposure:0.000} mb⁻¹" : "–";

    public string NeutronDensityText => Result is { } r ? $"{r.MeanNeutronDensity:0.0E+0} cm⁻³" : "–";

    public string PeakDensityText => Result is { } r ? $"peak {r.PeakNeutronDensity:0.0E+0} cm⁻³" : string.Empty;

    public string Ne22Text => Result is { } r ? $"{r.Ne22BurnedFraction:P1}" : "–";

    public string Fe56Text => Result is { } r ? $"{r.Fe56DestroyedFraction:P1}" : "–";

    public bool AllNe22Burned => Result is { Ne22BurnedFraction: > 0.995 };

    public string ConditionsText => Result is { } r
        ? $"T = {r.Conditions.TemperatureGK:0.###} GK (kT = {r.Conditions.KTkeV:0.0} keV) · ρ = {r.Conditions.Density:0} g/cm³" +
          $"{(r.Conditions.DensityFromModel ? " (model)" : string.Empty)} · He burning {r.Conditions.DurationYears / 1e6:0.00} Myr" +
          $"{(r.Conditions.DurationFromModel ? " (model)" : string.Empty)}, neutron phase {r.Conditions.PhaseDurationYears / 1e3:0.#} kyr · " +
          $"⟨σv⟩ ²²Ne(α,n) = {r.Conditions.Ne22AlphaNRate:0.00E+0}, (α,γ) = {r.Conditions.Ne22AlphaGammaRate:0.00E+0} cm³ mol⁻¹ s⁻¹ · " +
          $"{r.Abundances.Count} nuclides, {r.StepsTaken} steps"
        : string.Empty;

    public string DataText => Result?.DataDescription ?? string.Empty;

    partial void OnStatusChanged(string value) => HasError = value.StartsWith("Error", StringComparison.Ordinal);

    partial void OnStableOnlyChanged(bool value) => RebuildTable();

    private bool CanRun() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        if (!TryParseOptional(DensityText, out var density) || !TryParseOptional(DurationText, out var durationMyr))
        {
            Status = "Error: density and He-burning time must be numbers (or empty for the model value).";
            return;
        }

        var parameters = new SProcessParameters
        {
            StellarMass = StellarMass,
            TemperatureGK = TemperatureGK,
            Metallicity = Metallicity,
            HeliumAtOnset = HeliumAtOnset,
            CarbonFractionOfAsh = CarbonFraction,
            DensityOverride = density,
            DurationYearsOverride = durationMyr * 1e6,
            MaxZ = MaxZ,
        };

        var errors = parameters.Validate();
        if (errors.Count > 0)
        {
            Status = "Error: " + string.Join(" ", errors);
            return;
        }

        _cts = new CancellationTokenSource();
        IsBusy = true;
        IsProgressIndeterminate = true;
        Progress = 0;
        Status = "Starting…";
        var progress = new Progress<SProcessProgress>(p =>
        {
            Status = p.Stage;
            IsProgressIndeterminate = p.Fraction is null;
            Progress = (p.Fraction ?? 0) * 100;
        });

        try
        {
            var stopwatch = Stopwatch.StartNew();
            Result = await _service.RunAsync(parameters, progress, _cts.Token);
            RebuildTable();
            Status = $"Done in {stopwatch.Elapsed.TotalSeconds:0.0} s: τ = {Result.NeutronExposure:0.000} mb⁻¹, " +
                     $"{Result.Ne22BurnedFraction:P0} of ²²Ne burned";
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

    private bool CanCancel() => IsBusy;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _cts?.Cancel();

    private bool CanExport() => Result is not null;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private void ExportCsv()
    {
        if (Result is not { } r)
            return;

        var name = $"sprocess_M{r.Parameters.StellarMass:0.#}_T{r.Parameters.TemperatureGK:0.###}GK.csv";
        var path = _fileDialogs.SaveFile("Export s-process abundances", "CSV files (*.csv)|*.csv", name);
        if (path is null)
            return;

        var c = CultureInfo.InvariantCulture;
        var csv = new StringBuilder();
        csv.AppendLine(c, $"# Weak s-process, one-zone He core: M = {r.Parameters.StellarMass} Msun, T = {r.Parameters.TemperatureGK} GK, Z = {r.Parameters.Metallicity}");
        csv.AppendLine(c, $"# tau = {r.NeutronExposure:R} /mb, mean n_n = {r.MeanNeutronDensity:R} cm-3, 22Ne burned = {r.Ne22BurnedFraction:R}");
        csv.AppendLine("Z,A,nuclide,half_life_s,macs_mb,X_initial,X_final,X_decayed,Y_over_Y0Fe56_final,Y_over_Y0Fe56_decayed");
        foreach (var a in r.Abundances)
        {
            csv.AppendLine(c,
                $"{a.Nuclide.Z},{a.Nuclide.A},{a.Nuclide},{(a.IsStable ? "stable" : a.HalfLifeSeconds.ToString("R", c))},{a.CaptureBarns * 1000:R}," +
                $"{a.InitialMassFraction:R},{a.FinalMassFraction:R},{a.DecayedMassFraction:R},{a.RelativeToInitialFe56:R},{a.DecayedRelativeToInitialFe56:R}");
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

    private void RebuildTable()
    {
        TableRows.Clear();
        if (Result is null)
            return;

        foreach (var a in Result.Abundances
                     .Where(a => !StableOnly || a.IsStable)
                     .Where(a => a.FinalMassFraction > 0 || a.DecayedMassFraction > 0 || a.InitialMassFraction > 0)
                     .OrderBy(a => a.Nuclide))
        {
            TableRows.Add(a);
        }
    }

    private static bool TryParseOptional(string text, out double? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
            return true;

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed)
            || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }
}
