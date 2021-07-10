using Microsoft.Extensions.Hosting;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.SerialInterceptor
{
  public class SerialInterceptor : IHostedService
  {
    private readonly SerialPort _in;
    private readonly SerialPort _out;

    public SerialInterceptor()
    {
      _in = new SerialPort();
      _out = new SerialPort();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
      _in.PortName = "COM10";
      _in.BaudRate = 38400; // 38400; // or 4800
      _in.Parity = Parity.None;
      _in.StopBits = StopBits.One;
      _in.Handshake = Handshake.None;
      _in.DataBits = 8;
      _in.RtsEnable = true;
      _in.DtrEnable = true;
      _in.Open();
      _in.DataReceived += DataReceivedIn;
      _in.PinChanged += _in_PinChanged;
      _in.ErrorReceived += _in_ErrorReceived;


      _out.PortName = "COM3";
      _out.BaudRate = 38400; // 38400; // or 4800
      _out.Parity = Parity.None;
      _out.StopBits = StopBits.One;
      _out.Handshake = Handshake.None;
      _out.DataBits = 8;
      _out.RtsEnable = true;
      _out.DtrEnable = true;
      _out.Open();
      _out.DataReceived += DataReceivedOut;
      _out.PinChanged += _out_PinChanged;
      _out.ErrorReceived += _out_ErrorReceived;



      return Task.CompletedTask;
    }

    private void _out_ErrorReceived(object sender, SerialErrorReceivedEventArgs e)
    {
      Log.Information("IN: error");
    }

    private void _in_ErrorReceived(object sender, SerialErrorReceivedEventArgs e)
    {
      Log.Information("OUT: error");
    }

    private void _in_PinChanged(object sender, SerialPinChangedEventArgs e)
    {
      Log.Information("IN: pin changed");

    }
    private void _out_PinChanged(object sender, SerialPinChangedEventArgs e)
    {
      Log.Information("OUT: pin changed");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
      _in.Close();
      _out.Close();

      return Task.CompletedTask;
    }


    private void DataReceivedIn(object sender, SerialDataReceivedEventArgs ev)
    {
      var response = new byte[_in.BytesToRead];
      _in.Read(response, 0, response.Length);

      Log.Information("IN: " + BitConverter.ToString(response));

      _out.Write(response, 0, response.Length);
    }
    private void DataReceivedOut(object sender, SerialDataReceivedEventArgs ev)
    {
      var response = new byte[_out.BytesToRead];
      _out.Read(response, 0, response.Length);

      Log.Information($"OUT ({response.Length}): " + BitConverter.ToString(response));

      _in.Write(response, 0, response.Length);
    }
  }
}
