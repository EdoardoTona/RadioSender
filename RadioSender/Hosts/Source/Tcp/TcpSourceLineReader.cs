using RadioSender.Helpers;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using Serilog;
using System;
using System.Collections.Generic;
using System.Text;

namespace RadioSender.Hosts.Source.Tcp
{
  /// <summary>
  /// Accumulates a TCP byte stream, splits it into lines (on CR/LF), parses each
  /// complete line with <see cref="FormatStringParser"/> and dispatches the punch.
  /// Stateful per connection (holds a partial-line buffer); not thread-safe, so
  /// callers must feed one connection's bytes from a single receive callback.
  /// </summary>
  internal sealed class TcpSourceLineReader
  {
    private const int MaxLineLength = 8192;

    private readonly FilterService _filterService;
    private readonly DispatcherService _dispatcherService;
    private readonly TcpSourceConfiguration _configuration;
    private readonly FormatStringParser? _parser;
    private readonly StringBuilder _line = new();

    public TcpSourceLineReader(
      FilterService filterService,
      DispatcherService dispatcherService,
      TcpSourceConfiguration configuration,
      FormatStringParser? parser)
    {
      _filterService = filterService;
      _dispatcherService = dispatcherService;
      _configuration = configuration;
      _parser = parser;
    }

    public void Feed(ReadOnlySpan<byte> bytes, string endpoint)
    {
      if (_parser == null)
        return;

      // Decode as UTF-8. We split on '\n'/'\r' ourselves so the {CRLF} literal in the
      // format doubles as the line delimiter regardless of CRLF vs LF on the wire.
      var text = Encoding.UTF8.GetString(bytes);

      foreach (var ch in text)
      {
        if (ch == '\n' || ch == '\r')
        {
          FlushLine(endpoint);
          continue;
        }

        _line.Append(ch);

        if (_line.Length > MaxLineLength)
        {
          Log.Warning("Tcp source {endpoint} line exceeded {max} chars without terminator, discarding", endpoint, MaxLineLength);
          _line.Clear();
        }
      }
    }

    public void Reset() => _line.Clear();

    private void FlushLine(string endpoint)
    {
      if (_line.Length == 0)
        return;

      var line = _line.ToString();
      _line.Clear();

      Punch? parsed;
      try
      {
        parsed = _parser!.TryParse(line, ResolveSourceId(endpoint));
      }
      catch (Exception e)
      {
        Log.Warning(e, "Tcp source {endpoint} error parsing line '{line}'", endpoint, line);
        return;
      }

      if (parsed == null)
      {
        Log.Warning("Tcp source {endpoint} could not parse line '{line}'", endpoint, line);
        return;
      }

      var punch = _filterService.Transform(_configuration.Filter, parsed);
      if (punch == null)
      {
        Log.Debug("Tcp source {endpoint} punch filtered out: '{line}'", endpoint, line);
        return;
      }

      _dispatcherService.PushDispatch(new PunchDispatch(new[] { punch }));
    }

    private string ResolveSourceId(string endpoint) =>
      string.IsNullOrWhiteSpace(_configuration.SourceId) ? $"Tcp:{endpoint}" : _configuration.SourceId!;
  }
}
