﻿using Microsoft.Extensions.Hosting;
using NetCoreServer;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using Serilog;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Source.SIRAP
{
  public class SirapServer : TcpServer, ISource, IHostedService
  {
    private readonly IFilter _filter = Filter.Invariant;
    private readonly SirapServerConfiguration _configuration;
    private readonly DispatcherService _dispatcherService;

    public SirapServer(
      IEnumerable<IFilter> filters,
      DispatcherService dispatcherService,
      SirapServerConfiguration configuration) : base(IPAddress.Any, configuration.Port ?? throw new ArgumentNullException(nameof(configuration)))
    {
      _dispatcherService = dispatcherService;
      _configuration = configuration;
      _filter = filters.GetFilter(_configuration.Filter);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
      Start();
      return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
      Stop();
      return Task.CompletedTask;
    }

    protected override TcpSession CreateSession() { return new TcpSirapSession(this); }

    protected override void OnError(SocketError error)
    {
      Log.Warning("Sirap server socket error {error}", error);
    }

    internal void OnReceived(TcpSirapSession session, ReadOnlySpan<byte> buffer)
    {
      try
      {
        if (buffer.Length < 20)
        {
          OnReceivedV1(session, buffer);
        }
        else
        {
          OnReceivedV2(session, buffer);
        }
      }
      catch (Exception e)
      {
        Log.Error(e, "Error Sirap OnReceived");
      }
    }

    internal void OnReceivedV1(TcpSirapSession session, ReadOnlySpan<byte> buffer)
    {
#pragma warning disable IDE0059 // Assegnazione non necessaria di un valore
      byte type = buffer[0]; // 0=punch, 255=Triggered time
      var codeNo = BitConverter.ToUInt16(buffer.Slice(1, 2));
      var chipNo = BitConverter.ToInt32(buffer.Slice(3, 4));
      var codeDay = BitConverter.ToInt32(buffer.Slice(7, 4)); // Day information from SI punch, sunday = 0
      var codeTime = BitConverter.ToInt32(buffer.Slice(11, 4));
#pragma warning restore IDE0059 // Assegnazione non necessaria di un valore

      var time = TimeSpan.FromMilliseconds(codeTime * 100);

      if (!ManageSpecialFlags(codeDay, codeTime, ref time, out CompetitorStatus competitorStatus, out bool isCancellation))
        return;

      var dt = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day) + time;

      var punch = _filter.Transform(
                     new Punch(
                     Card: chipNo.ToString(),
                     Control: codeNo,
                     ControlType: codeNo == 9 ? PunchControlType.Finish : PunchControlType.Unknown,
                     Time: dt,
                     SourceId: "Sirap", // TODO
                     Cancellation: isCancellation,
                     CompetitorStatus: competitorStatus
                     )
                  );

      if (punch != null)
        _dispatcherService.PushDispatch(new PunchDispatch(new[] { punch }));
    }

    internal void OnReceivedV2(TcpSirapSession session, ReadOnlySpan<byte> buffer)
    {
      if (session.Name == null)
      {
        byte nameLength = buffer[0];
        session.Name = Encoding.UTF8.GetString(buffer.Slice(1, nameLength));
      }

#pragma warning disable IDE0059 // Assegnazione non necessaria di un valore
      byte type = buffer[21]; // 0=punch, 255=Triggered time

      var codeNo = BitConverter.ToUInt16(buffer.Slice(22, 2));
      var chipNo = BitConverter.ToInt32(buffer.Slice(24, 4));
      var codeDay = BitConverter.ToInt32(buffer.Slice(28, 4)); // Day information from SI punch
      var codeTime = BitConverter.ToInt32(buffer.Slice(32, 4));
#pragma warning restore IDE0059 // Assegnazione non necessaria di un valore

      var time = TimeSpan.FromMilliseconds(codeTime * 10);

      if (!ManageSpecialFlags(codeDay, codeTime, ref time, out CompetitorStatus competitorStatus, out bool isCancellation))
        return;

      var dt = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day) + time;

      var punch = _filter.Transform(
                     new Punch(
                     Card: chipNo.ToString(),
                     Control: codeNo,
                     ControlType: codeNo == 9 ? PunchControlType.Finish : PunchControlType.Unknown,
                     Time: dt,
                     SourceId: "Sirap", // TODO
                     Cancellation: isCancellation,
                     CompetitorStatus: competitorStatus
                     )
                  );

      if (punch != null)
        _dispatcherService.PushDispatch(new PunchDispatch(new[] { punch }));
    }

    internal static bool ManageSpecialFlags(int codeDay, 
      int codeTime,
      ref TimeSpan time, 
      out CompetitorStatus competitorStatus, 
      out bool isCancellation)
    {
      isCancellation = false;
      competitorStatus = CompetitorStatus.Unknown;

      if (codeDay == 0xFF)
      {
        // codeDay = 0xFF special flag (agreed with Simon Harston) to inform the time has a special meaning:
        // 00:00:00:cancel previous event
        // 00:00:01:DNS
        // 00:00:02:DNF
        // 00:00:03:MP
        // 00:00:04:DSQ
        // 00:00:05:OverTime

        if (codeTime == 360000001) // documented as "no time" in Kramer original specification
        {
          isCancellation = true;
          time = DateTime.Now.TimeOfDay;
        }
        else if (time.TotalSeconds == 1)
        {
          competitorStatus = CompetitorStatus.DNS;
          time = DateTime.Now.TimeOfDay;
        }
        else if (time.TotalSeconds == 2)
        {
          competitorStatus = CompetitorStatus.DNF;
          time = DateTime.Now.TimeOfDay;
        }
        else if (time.TotalSeconds == 3)
        {
          competitorStatus = CompetitorStatus.MP;
          time = DateTime.Now.TimeOfDay;
        }
        else if (time.TotalSeconds == 4)
        {
          competitorStatus = CompetitorStatus.DSQ;
          time = DateTime.Now.TimeOfDay;
        }
        else if (time.TotalSeconds == 5)
        {
          competitorStatus = CompetitorStatus.OverTime;
          time = DateTime.Now.TimeOfDay;
        }

      }
      else
      {
        if (codeTime == 360000001)
          return false; // ignore 
      }

      return true;
    }
  }


  class TcpSirapSession : TcpSession
  {
    private string? _name;
    public string? Name
    {
      get => _name;
      set
      {
        if (_name == null)
        {
          _name = value;
          Log.Information("Sirap client {id} is {name}", Id, Name);
        }
      }
    }
    public TcpSirapSession(SirapServer server) : base(server) { }

    protected override void OnConnected()
    {
      Log.Information("Sirap client {id} connected", Id.ToString());
    }

    protected override void OnDisconnected()
    {
      Log.Information("Sirap client {id} disconnected", Name ?? Id.ToString());
    }

    protected override void OnReceived(byte[] buffer, long offset, long size)
    {
      var ros = new ReadOnlySpan<byte>(buffer, (int)offset, (int)size);
      ((SirapServer)Server).OnReceived(this, ros);
    }

    protected override void OnError(SocketError error)
    {
      Log.Warning("Sirap client {id} socket error {error}", Name ?? Id.ToString(), error);
    }
  }

}
