using RadioSender.Hosts.Common;
using RadioSender.Hosts.Protocol.Sportident;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RadioSender.Hosts.Protocol.TmF;

public static class TmFProtocol
{
  public static PunchDispatch? MessageToDispatch(
    byte[] data,
    out RxMsg? message,
    out string? serialText,
    out string? error)
  {
    message = null;
    serialText = null;
    error = null;

    if (data.Length < 18)
    {
      error = $"Received broken message of {data.Length} bytes";
      return null;
    }

    var header = new RxHeader(data);
    if (header.NumBytes < 18 || header.NumBytes > data.Length)
    {
      error = $"Invalid TmF message length {header.NumBytes}. Received {data.Length} bytes";
      return null;
    }

    if (header.PacketType == PacketType.Event)
    {
      if (data[17] == 0x09)
      {
        if (data.Length < 26)
        {
          error = $"Invalid TmF status message length {data.Length}";
          return null;
        }

        var packet = new RxGetStatus(header, data);
        message = packet;
        return new PunchDispatch(Nodes: [new NodeNew(header.OrigID.ToString(), null, header.Latency, header.RSSI_Percent)]);
      }

      if (data[17] == 0x20)
      {
        var packet = new RxGetPath(header, data);
        message = packet;

        var from = header.OrigID;
        var hopsCount = header.HopCounter == 0 ? 1 : header.HopCounter;

        var hops = new List<Hop>();
        var nodes = new List<NodeNew>
        {
          new(from.ToString(), null, header.Latency, header.RSSI_Percent)
        };

        var i = 1;
        foreach (var jump in packet.Jumps)
        {
          hops.Add(new Hop(from.ToString(), jump.ReceiverId.ToString(), header.Latency / hopsCount, jump.RSSI_Percent));
          nodes.Add(new NodeNew(jump.ReceiverId.ToString(), null, header.Latency - ((header.Latency / hopsCount) * i), jump.RSSI_Percent));
          from = jump.ReceiverId;
          i++;
        }

        return new PunchDispatch(Hops: hops, Nodes: nodes);
      }

      return null;
    }

    var dataPacket = new RxData(header, data);
    message = dataPacket;

    var punch = SportidentProtocol.MessageToPunch(dataPacket.RxSerData, header.OrigID.ToString());
    if (punch == null)
    {
      serialText = Encoding.ASCII.GetString(dataPacket.RxSerData);
      return null;
    }

    return new PunchDispatch(Punches: [punch]);
  }

  public static bool HasNotPrintableChars(byte[] inputList)
  {
    return inputList.Any(s => s != 0x0D && s != 0x0A && (s < 0x20 || s > 0x7E));
  }
}
