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
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private FileWriter? _fileWriter;

    private readonly FilterService _filterService;
    private readonly FileConfiguration _configuration;
    public FileTarget(
    FilterService filterService,
    FileConfiguration configuration)
    {
      _filterService = filterService;
      _configuration = configuration;

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

    public void Dispose()
    {
      _fileWriter?.Dispose();
    }

    public async Task SendDispatch(PunchDispatch dispatch, CancellationToken ct = default)
    {
      await Task.Yield();
      if (dispatch.Punches == null || _fileWriter == null || string.IsNullOrWhiteSpace(_configuration.Format))
        return;

      var punches = _filterService.Transform(_configuration.Filter, dispatch.Punches);

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
