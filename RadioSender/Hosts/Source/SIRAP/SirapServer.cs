using Microsoft.Extensions.Hosting;
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
  public class SirapServer : TcpServer, IHostedService
  {
    private readonly IFilter _filter = Filter.Invariant;
    private readonly SirapServerConfiguration _configuration;
    private readonly DispatcherService _dispatcherService;

    public SirapServer(
      IEnumerable<IFilter> filters,
      DispatcherService dispatcherService,
      SirapServerConfiguration configuration) : base(IPAddress.Any, configuration.Port)
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
      byte type = buffer[0]; // 0=punch, 255=Triggered time
      var codeNo = BitConverter.ToUInt16(buffer.Slice(1, 2));
      var chipNo = BitConverter.ToInt32(buffer.Slice(3, 4));
      var codeDay = BitConverter.ToInt32(buffer.Slice(7, 4)); // Day information from SI punch
      var codeTime = BitConverter.ToInt32(buffer.Slice(11, 4));

      if (codeTime == 360000001)
      {
        // no time flag (invalid time, invalid punch...)
        return;
      }

      var time = TimeSpan.FromMilliseconds(codeTime * 100);

      var dt = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day) + time;

      _dispatcherService.PushPunch(
         _filter.Transform(
           new Punch()
           {
             Card = chipNo.ToString(),
             Control = codeNo,
             ControlType = codeNo == 9 ? PunchControlType.Finish : PunchControlType.Unknown,
             Time = dt
           }
         ));
    }

    internal void OnReceivedV2(TcpSirapSession session, ReadOnlySpan<byte> buffer)
    {
      if (session.Name == null)
      {
        byte nameLength = buffer[0];
        session.Name = Encoding.UTF8.GetString(buffer.Slice(1, nameLength));
      }

      byte type = buffer[21]; // 0=punch, 255=Triggered time

      var codeNo = BitConverter.ToUInt16(buffer.Slice(22, 2));
      var chipNo = BitConverter.ToInt32(buffer.Slice(24, 4));
      var codeDay = BitConverter.ToInt32(buffer.Slice(28, 4)); // Day information from SI punch
      var codeTime = BitConverter.ToInt32(buffer.Slice(32, 4));

      if (codeTime == 360000001)
      {
        // no time flag (invalid time, invalid punch...)
        return;
      }

      var time = TimeSpan.FromMilliseconds(codeTime * 10);

      var dt = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day) + time;

      _dispatcherService.PushPunch(
         _filter.Transform(
           new Punch()
           {
             Card = chipNo.ToString(),
             Control = codeNo,
             ControlType = codeNo == 9 ? PunchControlType.Finish : PunchControlType.Unknown,
             Time = dt
           }
         ));
    }
  }


  class TcpSirapSession : TcpSession
  {
    private string _name = null;
    public string Name
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
