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
  public sealed class FileTarget(
    FilterService filterService,
    FileConfiguration configuration) : ITarget, IDisposable
  {
    private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

    private FileWriter? _fileWriter;

    public void Dispose()
    {
      _fileWriter?.Dispose();
    }

    public async Task SendDispatch(PunchDispatch dispatch, CancellationToken ct = default)
    {
      await Task.Yield();
      if (dispatch.Punches == null || _fileWriter == null || string.IsNullOrWhiteSpace(configuration.Format))
        return;

      var punches = filterService.Transform(configuration.Filter, dispatch.Punches);

      if (!punches.Any())
        return;

      await _semaphore.WaitAsync(ct);
      try
      {
        foreach (var punch in punches)
        {
          string record = FormatStringHelper.GetString(punch, configuration.Format);
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
