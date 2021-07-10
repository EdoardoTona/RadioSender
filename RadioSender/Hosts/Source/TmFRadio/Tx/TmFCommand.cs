namespace RadioSender.Hosts.Source.TmFRadio
{
  public enum TmFCommand
  {
    GetNid = 0x10,
    GetStatus = 0x11,
    GetDidStatus = 0x12,
    GetConfigurationMemory = 0x13,
    GetCalibrationMemory = 0x14,
    ForceRouterReset = 0x15,
    GetPacketPath = 0x16,
  }
}
