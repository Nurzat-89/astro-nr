using AstroNucleaus.Domain.SProcess;

namespace AstroNucleaus.Application.SProcess;

/// <summary>
/// Runs weak s-process calculations; nuclear data is loaded once and kept in memory.
/// </summary>
public sealed class SProcessService
{
    private readonly ISProcessDataSource _dataSource;
    private readonly SemaphoreSlim _dataLock = new(1, 1);
    private SProcessNuclearData? _data;
    private int _dataMaxZ;

    public SProcessService(ISProcessDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<SProcessResult> RunAsync(
        SProcessParameters parameters,
        IProgress<SProcessProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var errors = parameters.Validate();
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(parameters));

        var data = await GetDataAsync(parameters.MaxZ, progress, cancellationToken).ConfigureAwait(false);

        progress?.Report(new SProcessProgress("Running the network", 0));
        var networkProgress = progress is null
            ? null
            : new InlineProgress<double>(f => progress.Report(new SProcessProgress("Running the network", f)));

        return await Task.Run(
            () => new SProcessCalculator(data).Run(parameters, networkProgress, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<SProcessNuclearData> GetDataAsync(
        int maxZ, IProgress<SProcessProgress>? progress, CancellationToken cancellationToken)
    {
        await _dataLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_data is null || _dataMaxZ < maxZ)
            {
                _data = await _dataSource.LoadAsync(maxZ, progress, cancellationToken).ConfigureAwait(false);
                _dataMaxZ = maxZ;
            }

            return _data;
        }
        finally
        {
            _dataLock.Release();
        }
    }

    /// <summary>Reports on the calling thread; the caller's IProgress marshals to the UI if needed.</summary>
    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
