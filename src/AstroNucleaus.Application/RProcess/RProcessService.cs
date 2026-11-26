using AstroNucleaus.Application.SProcess;
using AstroNucleaus.Domain.RProcess;

namespace AstroNucleaus.Application.RProcess;

/// <summary>
/// Runs r-process calculations; nuclear data is loaded once and reloaded after a new file is imported.
/// </summary>
public sealed class RProcessService
{
    private readonly IRProcessDataSource _dataSource;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private RProcessNuclearData? _data;

    public RProcessService(IRProcessDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public RProcessDataStatus GetStatus() => _dataSource.GetStatus();

    public async Task ImportReaclibAsync(string path, CancellationToken cancellationToken = default)
    {
        await _dataSource.ImportReaclibAsync(path, cancellationToken).ConfigureAwait(false);
        _data = null;
    }

    public async Task DownloadReaclibAsync(IProgress<SProcessProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        await _dataSource.DownloadReaclibAsync(progress, cancellationToken).ConfigureAwait(false);
        _data = null;
    }

    public async Task ImportMassTableAsync(string path, CancellationToken cancellationToken = default)
    {
        await _dataSource.ImportMassTableAsync(path, cancellationToken).ConfigureAwait(false);
        _data = null;
    }

    public void RemoveMassTable()
    {
        _dataSource.RemoveMassTable();
        _data = null;
    }

    public async Task<RProcessResult> RunAsync(
        RProcessParameters parameters,
        IProgress<SProcessProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var errors = parameters.Validate();
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(parameters));

        RProcessNuclearData data;
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            data = _data ??= await _dataSource.LoadAsync(progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }

        progress?.Report(new SProcessProgress("Running the network", 0));
        var networkProgress = progress is null
            ? null
            : new InlineProgress(f => progress.Report(new SProcessProgress(
                f < 0.9 ? "Neutron irradiation" : "Decay after freeze-out", f)));

        return await Task.Run(() => new RProcessCalculator(data).Run(parameters, networkProgress, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
    }

    private sealed class InlineProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
