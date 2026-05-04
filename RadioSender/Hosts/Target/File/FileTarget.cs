using RadioSender.Helpers;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Target.File
{
  public sealed class FileTarget : ITarget, IDisposable
  {
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private readonly FilterService _filterService;
    private readonly FileConfiguration _configuration;
    public FileTarget(
    FilterService filterService,
    FileConfiguration configuration)
    {
      _filterService = filterService;
      _configuration = configuration;
    }

    public void Dispose()
    {
      _semaphore?.Dispose();
    }

    public async Task SendDispatch(PunchDispatch dispatch, CancellationToken ct = default)
    {
      await Task.Yield();
      if (dispatch.Punches == null || _configuration.Path == null || string.IsNullOrWhiteSpace(_configuration.Format))
        return;

      var punches = _filterService.Transform(_configuration.Filter, dispatch.Punches);

      if (!punches.Any())
        return;

      await _semaphore.WaitAsync(ct);
      try
      {
        using var file = System.IO.File.Open(_configuration.Path, System.IO.FileMode.Append, System.IO.FileAccess.Write, System.IO.FileShare.Read);

        foreach (var punch in punches)
        {
          string record = FormatStringHelper.GetString(punch, _configuration.Format);
          file.Write(Encoding.UTF8.GetBytes(record));
        }
      }
      catch (Exception)
      {
        // Log the exception or handle it as needed
        Log.Error($"Error writing to file: {_configuration.Path}");
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
