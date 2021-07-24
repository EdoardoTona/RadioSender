using CsvHelper;
using CsvHelper.Configuration;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Target.File
{
  public class FileTarget : ITarget, IDisposable
  {
    private readonly FileConfiguration _configuration;
    private readonly FileInfo _fileInfo;
    private readonly FileFormat _format;
    private readonly IFilter _filter;

    private readonly StreamWriter _sr;
    private readonly CsvWriter _csv;
    private readonly SemaphoreSlim _semaphore;

    public FileTarget(
      IEnumerable<IFilter> filters,
      FileConfiguration configuration)
    {
      _configuration = configuration;
      _semaphore = new SemaphoreSlim(1, 1);
      _fileInfo = new FileInfo(_configuration.Path);

      _filter = filters.GetFilter(_configuration.Filter);

      if (!_fileInfo.Directory.Exists)
        Directory.CreateDirectory(_fileInfo.DirectoryName);

      var newFile = !_fileInfo.Exists;

      if (_configuration.Format == FileFormat.Auto)
      {
        switch (_fileInfo.Extension.ToLowerInvariant().Trim().Trim('.'))
        {
          case "csv": _format = FileFormat.Csv; break;
          default: throw new NotImplementedException();
        }
      }
      // TODO: handle network files (with broken connection)
      _sr = new StreamWriter(_fileInfo.FullName, append: true, Encoding.UTF8);

      switch (_format)
      {
        case FileFormat.Csv:
          _csv = new CsvWriter(_sr, new CsvConfiguration(CultureInfo.InvariantCulture)
          {
            HasHeaderRecord = false
          });
          if (newFile)
          {
            _csv.WriteHeader<Punch>();
            _csv.NextRecord();
          }
          break;
        default: throw new NotImplementedException();
      }
    }

    public void Dispose()
    {
      _csv?.Dispose();
      _sr?.Close();
      _sr?.Dispose();
      _semaphore?.Dispose();
    }

    public async Task SendPunch(Punch punch, CancellationToken ct = default)
    {
      await Task.Yield();
      punch = _filter.Transform(punch);
      if (punch == null) return;
      await _semaphore.WaitAsync();
      try
      {
        switch (_format)
        {
          case FileFormat.Csv:
            _csv.WriteRecord(punch);
            _csv.NextRecord();
            _csv.Flush();
            break;
        }
      }
      finally
      {
        _semaphore.Release();
      }
    }

    public async Task SendPunches(IEnumerable<Punch> punches, CancellationToken ct = default)
    {
      await Task.Yield();
      punches = _filter.Transform(punches);
      if (punches == null || !punches.Any()) return;
      await _semaphore.WaitAsync(ct);
      try
      {
        switch (_format)
        {
          case FileFormat.Csv:
            await _csv.WriteRecordsAsync(punches, ct);
            await _csv.FlushAsync();
            break;
        }
      }
      finally
      {
        _semaphore.Release();
      }
    }
  }
}
