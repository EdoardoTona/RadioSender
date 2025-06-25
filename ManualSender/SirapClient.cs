using RadioSender.Hosts.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Target.SIRAP
{
  public sealed class SirapClient
  {
    TcpClient? tcpClient;

    public void Disconnect()
    {
      tcpClient?.DisconnectAndStop();
      tcpClient = null;
    }

    public Task SendDispatch(string host, Punch punch, CancellationToken ct = default)
    {
      if (punch == null)
        return Task.CompletedTask;

      // Validate and parse host parameter
      if (!TryParseHost(host, out string address, out int port))
        throw new ArgumentException($"Invalid host format. Expected 'address:port', got '{host}'", nameof(host));

      if (address == "localhost")
        address = "127.0.0.1";

      if (tcpClient == null)
      {
        tcpClient = new TcpClient(address, port);
        tcpClient.Connect();
      }

      var buffer = GetBytes(punch, 2, TimeSpan.Zero);

      if (buffer == null || buffer.Length == 0)
        return Task.CompletedTask;

      tcpClient.Send(buffer);

      return Task.CompletedTask;
    }

    private static bool TryParseHost(string host, out string address, out int port)
    {
      address = string.Empty;
      port = 0;

      if (string.IsNullOrWhiteSpace(host))
        return false;

      // Check for protocol prefix and reject it
      if (host.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
          host.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
          host.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase))
        return false;

      // Split by last colon to handle IPv6 addresses
      int lastColonIndex = host.LastIndexOf(':');
      if (lastColonIndex == -1 || lastColonIndex == 0 || lastColonIndex == host.Length - 1)
        return false;

      string addressPart = host.Substring(0, lastColonIndex);
      string portPart = host.Substring(lastColonIndex + 1);

      // Validate port
      if (!int.TryParse(portPart, out port) || port <= 0 || port > 65535)
        return false;

      // Validate address part
      if (!IsValidAddress(addressPart))
        return false;

      address = addressPart;
      return true;
    }

    private static bool IsValidAddress(string address)
    {
      if (string.IsNullOrWhiteSpace(address))
        return false;

      // Try to parse as IP address first
      if (IPAddress.TryParse(address, out _))
        return true;

      // Validate as hostname
      // Basic hostname validation: no spaces, no protocol, contains valid characters
      if (address.Contains(' ') || address.Contains('/'))
        return false;

      // Check for valid hostname characters (letters, digits, hyphens, dots)
      for (int i = 0; i < address.Length; i++)
      {
        char c = address[i];
        if (!char.IsLetterOrDigit(c) && c != '.' && c != '-')
          return false;
      }

      // Hostname shouldn't start or end with hyphen or dot
      if (address.StartsWith('-') || address.EndsWith('-') ||
          address.StartsWith('.') || address.EndsWith('.'))
        return false;

      return true;
    }

    private static byte[]? GetBytes(Punch punch, int version, TimeSpan zeroTime)
    {
      if (!int.TryParse(punch.Card, out int chipNo))
        return null; // not numeric cards are not supported in SIRAP

      using var ms = new MemoryStream();
      using var bw = new BinaryWriter(ms);

      if (version == 2)
      {
        string name = "Manualsender";
        bw.Write((byte)name.Length);
        Span<byte> nameBuffer = new byte[20];
        Encoding.UTF8.GetBytes(name, nameBuffer);
        bw.Write(nameBuffer);
      }

      bw.Write((byte)0); // 0=punch, 255=Triggered time
      bw.Write((ushort)punch.Control);

      bw.Write(chipNo);

      int dayOfWeek = (int)punch.Time.DayOfWeek;
      int time;
      if (punch.Time == default)
      {
        time = 36000001; // invalid time
      }
      else
      {
        var punchTimeMs = punch.Time.TimeOfDay.TotalMilliseconds;

        if (punch.CompetitorStatus == CompetitorStatus.DNS)
        {
          dayOfWeek = 0xFF;
          punchTimeMs = 1000;
        }
        else if (punch.CompetitorStatus == CompetitorStatus.DNF)
        {
          dayOfWeek = 0xFF;
          punchTimeMs = 2000;
        }
        else if (punch.CompetitorStatus == CompetitorStatus.MP)
        {
          dayOfWeek = 0xFF;
          punchTimeMs = 3000;
        }
        else if (punch.CompetitorStatus == CompetitorStatus.DSQ)
        {
          dayOfWeek = 0xFF;
          punchTimeMs = 4000;
        }
        else if (punch.CompetitorStatus == CompetitorStatus.OverTime)
        {
          dayOfWeek = 0xFF;
          punchTimeMs = 5000;
        }

        if (version == 2)
        {
          // 1/100 resolution
          time = (int)punchTimeMs / 10 - (int)zeroTime.TotalMilliseconds / 10;
          if (time < 0)
            time += 100 * 3600 * 24;
        }
        else
        {
          // 1/10 resolution
          time = (int)punchTimeMs / 100 - (int)zeroTime.TotalMilliseconds / 100;
          if (time < 0)
            time += 10 * 3600 * 24;
        }
      }

      bw.Write(dayOfWeek); // Day information from SI punch, sunday = 0
      bw.Write(time);

      return ms.ToArray();
    }

  }


}
