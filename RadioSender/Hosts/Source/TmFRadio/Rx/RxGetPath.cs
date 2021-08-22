using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace RadioSender.Hosts.Source.TmFRadio
{
  public record Jump(byte Rssid, uint ReceiverId)
  {
    public int RSSI_Percent
    {
      get => RxHeader.ConvRSSI_Percent(Rssid);
    }
  }

  public record RxGetPath : RxMsg
  {
    public RxGetPath(RxHeader header, byte[] bufq)
    {
      Header = header;
      MessDetail = bufq[17]; // se contiene n.9 = Status mess IMA

      byte i = 18;
      while (bufq.Length >= i + 5)
      {
        Jumps.Add(new Jump(bufq[i], BinaryPrimitives.ReadUInt32LittleEndian(new ReadOnlySpan<byte>(bufq, ++i, 4))));
        i += 4;
      }

    }
    public byte MessDetail; // 17   Status Message IMA = 9

    public List<Jump> Jumps = new();
  }
}
