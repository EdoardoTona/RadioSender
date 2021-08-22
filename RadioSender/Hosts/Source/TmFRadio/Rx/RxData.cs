using System;

namespace RadioSender.Hosts.Source.TmFRadio
{
  public record RxData : RxMsg
  {
    public RxData(RxHeader header, byte[] bufq)
    {
      Header = header;
      SerDataBlockCounter = bufq[17]; // sempre 0 altrimenti blocco lungo parzializzato
      RxSerData = new byte[header.NumBytes - 18];
      Buffer.BlockCopy(bufq, 18, RxSerData, 0, RxSerData.Length);
    }

    public byte SerDataBlockCounter { get; } // 17   0: Single data block, terminated by UART time-out. 1-255: Block(partition) number in large data streams controlled by CTS or Xon/Xoff handshake
    public byte[] RxSerData { get; } // 18..119 serial data
  }
}
