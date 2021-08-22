using System;

namespace RadioSender.Hosts.Source.TmFRadio
{
  public record RxGetStatus : RxMsg
  {
    public RxGetStatus(RxHeader header, byte[] bufq)
    {
      Header = header;
      MessDetail = bufq[17]; // se contiene n.9 = Status mess IMA
      MessMSB = bufq[18];
      MessLSB = bufq[19];
      AddrIdData = BitConverter.ToUInt32(bufq, 20);
      Temperat_C = bufq[24] - 128;
      Voltage_V = bufq[25] * 0.03;

      if (MessDetail > 19)
        MessDetail = 19;
    }
    public byte MessDetail; // 17   Status Message IMA = 9
    public byte MessMSB;    // 18   0: 0 (Default); 5: Device Connection status:1.No alternative;2.Alternative;3.Single gateway
    public byte MessLSB;    // 19   9. Configurable content ref.
    public uint AddrIdData; // 20   9. Configurable content ref.

    /// <summary>
    /// no index    Temperature corrected
    /// </summary>
    public double Temperat_C { get; set; }
    /// <summary>
    /// no index    Voltage corrected
    /// </summary>
    public double Voltage_V { get; set; }

    public string EventDetailString { get { return EventDetailDescripion[MessDetail]; } }


    private static readonly string[] EventDetailDescripion =
    {
            "Not used",                                         //  0
            "Digital Input Change Detected",                    //  1
            "Analogue 0 Input Trig",                            //  2
            "Analogue 1 Input Trig",                            //  3
            "Not used",
            "Not used",
            "RF Jamming Detected",                              //  6
            "Not used",
            "Device Reset",                                     //  8
            "Status message",                                   //  9
            "Channel is Busy with Similar System ID",           //  10
            "Channel is Free",                                  //  11
            "Channel is Jammed",                                //  12    
            "Other Tinymesh™ System Active on this Channel",    //  13
            "Not used",
            "Not used",
            "Command Received and Executed (ACK)11",            //  16
            "Command Rejected, Not Executed(NAK)11",            //  17
            "Status Message(NID)",                              //  18
            "Status Message Next Receiver"                      //  19
        };
  }
}
