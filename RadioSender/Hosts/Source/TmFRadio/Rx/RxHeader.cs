using System;
using System.Buffers.Binary;

namespace RadioSender.Hosts.Source.TmFRadio
{
  public enum PacketType
  {
    Unknown = 0,
    Event = 0x02,
    SerialData = 0x10
  }
  public record RxHeader
  {
    public RxHeader(byte[] bufq)
    {
      PacketReceived = DateTimeOffset.UtcNow;

      NumBytes = bufq[0];
      SysID = BinaryPrimitives.ReadUInt32LittleEndian(new ReadOnlySpan<byte>(bufq, 1, 4)); // BitConverter.ToUInt32(bufq, 0); // little endian??
      OrigID = BinaryPrimitives.ReadUInt32LittleEndian(new ReadOnlySpan<byte>(bufq, 5, 4)); //BitConverter.ToUInt32(bufq, 4); // little endian??
      OrigRSSI = bufq[9];
      ONL = bufq[10];
      HopCounter = bufq[11];
      MessCounter = BinaryPrimitives.ReadUInt16BigEndian(new ReadOnlySpan<byte>(bufq, 12, 2)); // BitConverter.ToUInt16(new byte[] { bufq[12], bufq[11] }, 0); // big endian!
      Latency = BinaryPrimitives.ReadUInt16BigEndian(new ReadOnlySpan<byte>(bufq, 14, 2));  //BitConverter.ToUInt16(new byte[] { bufq[14], bufq[13] }, 0);  // big endian!

      PacketType = (PacketType)bufq[16];
    }

    public DateTimeOffset PacketReceived;

    /// <summary>
    /// index 0    number of bytes
    /// </summary>
    public int NumBytes;
    /// <summary>
    /// index 1    system ID identico per tutta la rete (unsigned int 4 Bytes)
    /// </summary>
    public uint SysID;
    /// <summary>
    /// index 5    origine ID identificatore dispositivo
    /// </summary>
    public uint OrigID;
    /// <summary>
    /// index 9    RSSI from origin device
    /// </summary>
    public byte OrigRSSI;
    /// <summary>
    /// index 10   origin network level 'Hop' level, number of vertical hops to reach Gateway
    /// </summary>
    public byte ONL;
    /// <summary>
    /// index 11   Number of actual hops from Router to Gateway
    /// </summary>
    public byte HopCounter;
    /// <summary>
    /// index 12   Unique number maintained by originating node (unsigned short 2Bytes)
    /// </summary>
    public ushort MessCounter;
    /// <summary>
    /// index 14   Latency Counter resolution 10ms
    /// </summary>
    public ushort Latency;
    /// <summary>
    /// index 16   Event 2 (0x02) or Serial data in 16 (0x10)
    /// </summary>
    public PacketType PacketType;
    /// <summary>
    /// no index    RSSI from origin calculated 
    /// </summary>
    public double RSSI_dBm { get { return (Convert.ToDouble(OrigRSSI) / 2) * -1; } }
    /// <summary>
    /// no index    RSSI from origin converted in percent
    /// </summary>
    public int RSSI_Percent { get { return ConvRSSI_Percent(OrigRSSI); } }

    public static int ConvRSSI_Percent(byte _bRSSI)
    {
      float rssi = Convert.ToInt32(_bRSSI);
      float deltaRSSI = 190 - 46; // (190 = segnale minimo, 20 = saturazione                      
      rssi -= 46; // rssi - minimo valore
      float res = (rssi / deltaRSSI) * 100;

      int risp = 100 - (int)res;
      if (risp > 99) risp = 100;
      if (risp < 0) risp = 0;
      return risp;
    }
  }
}
