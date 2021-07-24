using CsvHelper;
using CsvHelper.Configuration;
using RadioSender.Hosts.Common;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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

    private readonly StreamWriter _sr;
    private readonly CsvWriter _csv;
    private readonly SemaphoreSlim _semaphore;

    public FileTarget(FileConfiguration configuration)
    {
      _configuration = configuration;
      _semaphore = new SemaphoreSlim(1, 1);
      _fileInfo = new FileInfo(_configuration.Path);

      if (!_fileInfo.Directory.Exists)
        Directory.CreateDirectory(_fileInfo.DirectoryName);

      var newFile = !_fileInfo.Exists;

      if (_configuration.Format == FileFormat.Auto)
      {
        switch (_fileInfo.Extension.ToLowerInvariant().Trim().Trim('.'))
        {
          case "csv": _format = FileFormat.Csv; break;
          case "json": _format = FileFormat.Json; break;
          case "xml": _format = FileFormat.Xml; break;
        }
      }

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
