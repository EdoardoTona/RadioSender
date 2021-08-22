using RadioSender.Helpers;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Target.File
{
  public sealed class FileTarget : ITarget, IDisposable
  {
    private readonly SemaphoreSlim _semaphore;

    private IFilter _filter = Filter.Invariant;
    private FileConfiguration _configuration;

    private FileWriter? _fileWriter;

    public FileTarget(
      IEnumerable<IFilter> filters,
      FileConfiguration configuration)
    {
      _configuration = configuration;
      _semaphore = new SemaphoreSlim(1, 1);
      UpdateConfiguration(filters, configuration);
    }

    public void Dispose()
    {
      _fileWriter?.Dispose();
    }

    public void UpdateConfiguration(IEnumerable<IFilter> filters, Configuration configuration)
    {
      Interlocked.Exchange(ref _configuration!, configuration as FileConfiguration);
      Interlocked.Exchange(ref _filter, filters.GetFilter(_configuration.Filter));

      _semaphore.Wait();

      try
      {
        _fileWriter?.Dispose();
        if (!string.IsNullOrEmpty(_configuration.Path))
          _fileWriter = new FileWriter(_configuration.Path);
      }
      finally
      {
        _semaphore.Release();
      }
    }


    public async Task SendDispatch(PunchDispatch dispatch, CancellationToken ct = default)
    {
      await Task.Yield();
      if (_fileWriter == null || string.IsNullOrWhiteSpace(_configuration.Format))
        return;

      var punches = _filter.Transform(dispatch.Punches);

      if (!punches.Any())
        return;

      await _semaphore.WaitAsync(ct);
      try
      {
        foreach (var punch in punches)
        {
          string record = FormatStringHelper.GetString(punch, _configuration.Format);
          _fileWriter.Write(record);
        }
      }
      finally
      {
        _semaphore.Release();
      }
    }

    public async Task SendDispatches(IEnumerable<PunchDispatch> dispatches, CancellationToken ct = default)
    {
      foreach (var dispatch in dispatches)
        await SendDispatch(dispatch, ct);
    }

  }
}
