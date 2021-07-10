using CsvHelper.Configuration.Attributes;
using System;

namespace RadioSender.Hosts.Source.ROC
{
  public class ROCPunch
  {
    [Index(0)]
    public long Id { get; set; }
    [Index(1)]
    public int Code { get; set; }
    [Index(2)]
    public long Card { get; set; }
    [Index(3)]
    public DateTime Time { get; set; }
  }

}
