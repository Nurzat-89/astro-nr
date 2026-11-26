namespace AstroNucleaus.Wpf.ViewModels;

/// <summary>How playback time maps onto the run.</summary>
public enum PlaybackPace
{
    /// <summary>Uniform in time.</summary>
    Time,

    /// <summary>Uniform in log(time): stretches fast early bursts.</summary>
    LogTime,

    /// <summary>Uniform in neutron exposure τ.</summary>
    Exposure,

    /// <summary>Half of the playback for each of two phases (e.g. irradiation, then decay), log time within each.</summary>
    Phases,
}

public sealed record PlaybackPaceOption(PlaybackPace Pace, string Name);
