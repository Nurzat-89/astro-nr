using System.Diagnostics;
using System.Windows.Threading;
using AstroNucleaus.Domain.SProcess;
using AstroNucleaus.Wpf.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AstroNucleaus.Wpf.ViewModels;

/// <summary>A nuclide shown in the animation.</summary>
public sealed record PlaybackNuclide(Nuclide Nuclide, bool IsStable, string Details);

/// <summary>Everything the player needs: nuclides, frames and how to describe a moment.</summary>
/// <param name="Times">Time of each frame (any unit; <paramref name="FormatTime"/> formats it).</param>
/// <param name="Frames">Abundance of every nuclide at each frame (index i ↔ <paramref name="Nuclides"/>[i]).</param>
/// <param name="Exposure">Neutron exposure per frame, to pace by exposure; null hides that option.</param>
/// <param name="Describe">Text for the moment between frames k and k+1 at fraction f.</param>
/// <param name="InitialFrame">Frame shown after loading.</param>
public sealed record PlaybackData(
    string Title,
    string ValueLabel,
    IReadOnlyList<PlaybackNuclide> Nuclides,
    IReadOnlyList<double> Times,
    IReadOnlyList<double[]> Frames,
    IReadOnlyList<double>? Exposure,
    Func<int, double, string> Describe,
    Func<double, string> FormatTime,
    int InitialFrame)
{
    /// <summary>Last frame of the first phase, to offer the "half each" pace; null for a single phase.</summary>
    public int? PhaseBoundary { get; init; }

    /// <summary>Names of the two phases for the pace option, e.g. "Irradiation | decay".</summary>
    public string PhasesName { get; init; } = "Phase 1 | phase 2";

    public PlaybackPace DefaultPace { get; init; } = PlaybackPace.LogTime;
}

/// <summary>
/// Plays the evolution of isotope abundances on the chart of nuclides. Between recorded frames the abundances
/// are interpolated in log space, so the colours change smoothly; the colour scale is fixed for the whole run.
/// </summary>
public partial class NuclidePlaybackViewModel : ObservableObject
{
    private const double Floor = 1e-30;

    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();
    private PlaybackData? _data;
    private double[] _axis = [];

    public NuclidePlaybackViewModel()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(30) };
        _timer.Tick += OnTick;
    }

    public IReadOnlyList<double> SpeedOptions { get; } = [5, 10, 20, 40];

    [ObservableProperty]
    private IReadOnlyList<PlaybackPaceOption> _paceOptions = [];

    /// <summary>Playback position from 0 (first frame) to 1 (last frame).</summary>
    [ObservableProperty]
    private double _progress = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayGlyph))]
    [NotifyPropertyChangedFor(nameof(PlayText))]
    private bool _isPlaying;

    [ObservableProperty]
    private double _playbackSeconds = 10;

    [ObservableProperty]
    private PlaybackPace _pace = PlaybackPace.LogTime;

    [ObservableProperty]
    private IReadOnlyList<NuclideCell>? _cells;

    [ObservableProperty]
    private double _scaleTop = double.NaN;

    [ObservableProperty]
    private string _title = "Isotope abundances on the chart of nuclides";

    [ObservableProperty]
    private string _valueLabel = "Y/Y₀";

    [ObservableProperty]
    private string _timeText = string.Empty;

    [ObservableProperty]
    private string _stateText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TogglePlayCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestartCommand))]
    private bool _hasFrames;

    public string PlayGlyph => IsPlaying ? "" : "";

    public string PlayText => IsPlaying ? "Pause" : "Play";

    /// <summary>Shows a new run (null clears the player).</summary>
    public void Load(PlaybackData? data)
    {
        Stop();
        _data = data;
        HasFrames = data is { Frames.Count: > 1 } && data.Frames.Count == data.Times.Count;
        if (!HasFrames)
        {
            Cells = null;
            TimeText = StateText = string.Empty;
            return;
        }

        Title = data!.Title;
        ValueLabel = data.ValueLabel;
        var options = new List<PlaybackPaceOption>();
        if (data.PhaseBoundary is { } boundary && boundary > 0 && boundary < data.Frames.Count - 1)
            options.Add(new(PlaybackPace.Phases, $"{data.PhasesName} (half each)"));
        options.Add(new(PlaybackPace.LogTime, "Time, log scale"));
        options.Add(new(PlaybackPace.Time, "Time, linear"));
        if (data.Exposure is not null)
            options.Add(new(PlaybackPace.Exposure, "Neutron exposure τ"));
        PaceOptions = options;
        Pace = options.Any(o => o.Pace == data.DefaultPace) ? data.DefaultPace : options[0].Pace;

        var max = data.Frames.SelectMany(f => f).DefaultIfEmpty(1).Max();
        ScaleTop = Math.Ceiling(Math.Log10(Math.Max(max, Floor)));

        BuildAxis();
        var initial = Math.Clamp(data.InitialFrame, 0, data.Frames.Count - 1);
        var span = _axis[^1] - _axis[0];
        Progress = span > 0 ? (_axis[initial] - _axis[0]) / span : 1;
        Render();
    }

    partial void OnProgressChanged(double value) => Render();

    partial void OnPaceChanged(PlaybackPace value)
    {
        BuildAxis();
        Render();
    }

    private bool CanPlay() => HasFrames;

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private void TogglePlay()
    {
        if (IsPlaying)
        {
            Stop();
            return;
        }

        if (Progress >= 1)
            Progress = 0;
        IsPlaying = true;
        _clock.Restart();
        _timer.Start();
    }

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private void Restart()
    {
        Progress = 0;
        if (!IsPlaying)
            TogglePlay();
    }

    public void Stop()
    {
        _timer.Stop();
        _clock.Stop();
        IsPlaying = false;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var elapsed = _clock.Elapsed.TotalSeconds;
        _clock.Restart();
        var next = Progress + elapsed / Math.Max(1, PlaybackSeconds);
        if (next >= 1)
        {
            Progress = 1;
            Stop();
        }
        else
        {
            Progress = next;
        }
    }

    /// <summary>Monotonic playback axis per frame: time, log time, or neutron exposure.</summary>
    private void BuildAxis()
    {
        if (_data is null)
            return;

        var times = _data.Times;
        var first = times.FirstOrDefault(t => t > 0, 1.0);
        _axis = Pace switch
        {
            PlaybackPace.Exposure when _data.Exposure is { } exposure && exposure[^1] > 0 => exposure.ToArray(),
            PlaybackPace.LogTime => times.Select(t => Math.Log10(t + first)).ToArray(),
            PlaybackPace.Phases when _data.PhaseBoundary is { } b && b > 0 && b < times.Count - 1 => PhaseAxis(times, b),
            _ => times.ToArray(),
        };
    }

    /// <summary>0–0.5 over the first phase and 0.5–1 over the second, each in log time from its own start.</summary>
    private static double[] PhaseAxis(IReadOnlyList<double> times, int boundary)
    {
        var axis = new double[times.Count];
        Fill(0, boundary, 0.0);
        Fill(boundary, times.Count - 1, 0.5);
        return axis;

        void Fill(int from, int to, double offset)
        {
            var start = times[from];
            var first = Enumerable.Range(from, to - from + 1).Select(i => times[i] - start).FirstOrDefault(t => t > 0, 1.0);
            double Log(int i) => Math.Log10(times[i] - start + first);
            var low = Log(from);
            var span = Math.Max(Log(to) - low, 1e-12);
            for (var i = from; i <= to; i++)
                axis[i] = offset + 0.5 * (Log(i) - low) / span;
        }
    }

    /// <summary>Fractional frame index for the current progress along the playback axis.</summary>
    private double FramePosition()
    {
        if (_axis.Length < 2 || _axis[^1] <= _axis[0])
            return Progress * (_data!.Frames.Count - 1);

        var target = _axis[0] + Progress * (_axis[^1] - _axis[0]);
        var k = Array.FindIndex(_axis, v => v >= target);
        if (k <= 0)
            return 0;
        var span = _axis[k] - _axis[k - 1];
        return k - 1 + (span > 0 ? (target - _axis[k - 1]) / span : 1);
    }

    private void Render()
    {
        if (_data is null || !HasFrames)
            return;

        var position = Math.Clamp(FramePosition(), 0, _data.Frames.Count - 1);
        var k = Math.Min((int)Math.Floor(position), _data.Frames.Count - 2);
        var f = position - k;
        var a = _data.Frames[k];
        var b = _data.Frames[k + 1];

        var cells = new NuclideCell[a.Length];
        for (var i = 0; i < a.Length; i++)
        {
            var value = a[i] < Floor && b[i] < Floor
                ? 0
                : Math.Exp((1 - f) * Math.Log(Math.Max(a[i], Floor)) + f * Math.Log(Math.Max(b[i], Floor)));
            var nuclide = _data.Nuclides[i];
            cells[i] = new NuclideCell(nuclide.Nuclide, value, nuclide.IsStable, nuclide.Details);
        }

        Cells = cells;

        var time = _data.Times[k] + (_data.Times[k + 1] - _data.Times[k]) * f;
        TimeText = $"t = {_data.FormatTime(time)} of {_data.FormatTime(_data.Times[^1])}";
        StateText = _data.Describe(k, f);
    }
}
