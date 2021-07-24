using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Source.SportidentSerial
{
  public static class SportidentStationInfo
  {

    private static readonly List<string> bs11LoopAntennaSn = new(new string[]
      {
        "300818",
        "301101",
        "301102",
        "301103",
        "301104",
        "301105",
        "301410",
        "301411",
        "301568",
        "301569",
        "301570",
        "301571",
        "301572",
        "301573",
        "301734",
        "301735",
        "301743",
        "301744",
        "301745",
        "301875",
        "301876",
        "301920",
        "302065",
        "302066",
        "302354",
        "302355",
        "302356",
        "302357",
        "302358",
        "302359",
        "302463",
        "302531",
        "302534",
        "302535",
        "302536",
        "302568",
        "302569",
        "302624",
        "302625",
        "302626",
        "302627",
        "302652",
        "302653",
        "302654",
        "302655",
        "302656",
        "302657",
        "302658",
        "302692",
        "302730",
        "302762",
        "302763",
        "302764",
        "302765",
        "302775",
        "302790",
        "302791",
        "302843",
        "302853",
        "302854",
        "302855",
        "302856",
        "302857",
        "302914",
        "303004",
        "303005",
        "303006",
        "303068",
        "303069",
        "303079",
        "303085",
        "303129",
        "303130",
        "303131",
        "303132",
        "303142",
        "303157",
        "303204",
        "303205",
        "303206",
        "303217",
        "303219",
        "303249",
        "303250",
        "303251",
        "303252",
        "303253",
        "303310"
      });
    public static SportidentProduct GetSportidentProductInfo(byte[] data)
    {
      var controlCode = BinaryPrimitives.ReadUInt16BigEndian(new byte[] { (byte)(data[0] & 0b_0111_1111), data[1] });

      var srrChannel = (SrrChannel)data[55];
      var fw = Encoding.GetEncoding("iso8859-1").GetString(new byte[3]
      {
          data[8],
          data[9],
          data[10]
      });
      int result;
      var firmware = int.TryParse(fw, out result) ? result : 0;

      int num1 = (int)data[11];
      int num2 = (int)data[12];
      int num3 = (int)data[13];
      var result1 = new DateTime(2000, 1, 1);
      if (num1 >= 0 && num1 <= DateTime.Now.Year - 2000)
        num1 += 2000;
      else if (num1 >= DateTime.Now.Year - 2000 + 1 && num1 <= 99)
        num1 += 1900;
      DateTime.TryParse(string.Format("{0:0000}-{1:00}-{2:00}", (object)num1, (object)num2, (object)num3), out result1);
      var productionDate = result1;

      return GetProduct(data[3], data[4], data[5], data[6], data[15], data[14], data[7], controlCode, productionDate, firmware, srrChannel);
    }


    private static SportidentProduct GetProduct(
      byte sn0,
      byte sn1,
      byte sn2,
      byte sn3,
      byte cfg0,
      byte cfg1,
      byte cfg2,
      ushort code,
      DateTime productDate,
      int firmware,
      SrrChannel channel

      )
    {
      ProductType ProductType = ProductType.NotSet;
      ProductInterface ProductInterface = ProductInterface.NotSet;
#pragma warning disable CS0219 // La variabile è assegnata, ma il suo valore non viene mai usato
      var attachedSrrModule = false;
#pragma warning restore CS0219 // La variabile è assegnata, ma il suo valore non viene mai usato
      bool flag1 = ((int)cfg0 & 128) == 128;
      string str1 = string.Empty;
      string str2 = string.Empty;
      string str3 = string.Empty;
      string str4 = string.Empty;
      bool flag2 = ((int)cfg1 & 32) == 32;
      bool flag3 = ((int)cfg1 & 16) == 16;
      bool flag4 = ((int)cfg1 & 8) == 8;
      bool flag5 = ((int)cfg1 & 4) == 4;
      ushort ProductConfiguration = BitConverter.ToUInt16(new byte[2]
  {
        cfg0,
        cfg1
  }, 0);
      ProductFamily ProductFamily = Enum.IsDefined(typeof(ProductFamily), (int)cfg0) ? (ProductFamily)cfg0 : ProductFamily.NotSet;

      string SerialNumber;
      if (ProductFamily == ProductFamily.SimSrr)
      {
        uint num = BitConverter.ToUInt32(new byte[4]
        {
            sn3,
            sn2,
            sn1,
            0b_0
        }, 0);
        if (num == 16777215U)
          num = 0U;
        SerialNumber = num.ToString((IFormatProvider)CultureInfo.InvariantCulture);
      }
      else
      {
        uint num = BitConverter.ToUInt32(new byte[4]
        {
            sn3,
            sn2,
            sn1,
            sn0
        }, 0);
        if (num == uint.MaxValue)
          num = 0U;
        SerialNumber = num.ToString((IFormatProvider)CultureInfo.InvariantCulture);
      }



      byte BoardVersion = (byte)(cfg0 & 15U);
      switch (ProductFamily)
      {
        case ProductFamily.SimSrr:
          ProductType = ProductType.SimSrr;
          ProductInterface = ProductInterface.UsbVcp; ; // ProductInterface.UsbHid;
          str1 = "SRR";
          switch (cfg1)
          {
            case 106:
              str2 = " ED_AH (ActiveCard)";
              break;
            case 107:
              str2 = " ED_LDK (BS)";
              break;
            case 111:
              str2 = " AP (Dongle)";
              break;
          }
          break;
        case ProductFamily.SiPoint2:
          ProductInterface = ProductInterface.UsbVcp;
          if (cfg1 == (byte)144)
          {
            ProductType = ProductType.SiPointGolf2;
            str1 = "SI-Point Golfbox 2";
            break;
          }
          str1 = "SI-Point 2";
          break;
        case ProductFamily.Bs8SiMaster:
          ProductType = ProductType.Bs8SiMaster;
          str1 = string.Format("{0}{1}", flag1 ? (object)"BSM" : (object)"BSF", (object)BoardVersion);
          str4 += " Master";
          break;
        case ProductFamily.Bsx8Ostarter:
        case ProductFamily.Bsx7:
        case ProductFamily.Bsx8:
          switch (cfg1)
          {
            case 129:
              switch (ProductFamily)
              {
                case ProductFamily.Bsx8Ostarter:
                  BoardVersion = (byte)8;
                  str4 = " O-Starter";
                  ProductType = ProductType.Bsf8Ostarter;
                  break;
                case ProductFamily.Bsx7:
                  ProductType = ProductType.Bsf7;
                  break;
                case ProductFamily.Bsx8:
                  ProductType = ProductType.Bsf8;
                  break;
              }
              break;
            case 145:
              ProductType = ProductFamily == ProductFamily.Bsx8 ? ProductType.Bsm8 : ProductType.Bsm7;
              ProductInterface = ProductType == ProductType.Bsm7 ? ProductInterface.Rs232 : ProductInterface.UsbVcp;
              break;
            case 149:
              ProductInterface = ProductInterface.Rs232;
              ProductType = ProductType.Bs7S;
              break;
            case 177:
              ProductInterface = ProductInterface.Rs232;
              ProductType = ProductType.Bs7P;
              break;
          }
          if (flag2)
            str2 = "-P";
          if (flag5)
            str2 += string.IsNullOrEmpty(str2) ? "-S" : "S";
          if (flag3 | flag4)
          {
            if (((int)cfg2 & 56) == 48)
              ProductInterface = ProductInterface.UsbVcp;
            if (((int)cfg2 & 7) == 6)
              ProductInterface = ProductInterface.UsbVcp;
            str4 = " UART";
            if (flag4)
              str4 = str4 + "0" + GetUartType(cfg2, (byte)0);
            if (flag3)
            {
              if (flag4)
                str4 += " + UART";
              str4 = str4 + "1" + GetUartType(cfg2, (byte)1);
            }
          }
          else
            flag1 = false;
          if (ProductType == ProductType.Bsm8 && ((int)cfg2 & 56) != 48)
          {
            attachedSrrModule = true;
            ProductType = ProductType.Bsf8;
            str4 = " SRR";
            flag1 = false;
          }
          str1 = string.Format("{0}{1}", flag1 ? (object)"BSM" : (object)"BSF", (object)BoardVersion);
          break;
        case ProductFamily.Bs11LoopAntenna:
          ProductType = ProductType.Bs11LoopAntenna;
          BoardVersion = (byte)11;
          ProductInterface = ProductInterface.UsbHid; // ProductInterface.UsbVcp;
          str1 = "BS11 Loop Antenna";
          break;
        case ProductFamily.Bs11Large:
          ProductType = ProductType.Bs11Large;
          BoardVersion = (byte)11;
          ProductInterface = ProductInterface.UsbHid; // ProductInterface.UsbVcp;
          str1 = "BS11 Large";
          break;
        case ProductFamily.Bs11Small:
          ProductType = ProductType.Bs11Small;
          BoardVersion = (byte)11;
          ProductInterface = ProductInterface.UsbHid; // ProductInterface.UsbVcp;
          str1 = "BS11 Small";
          if (bs11LoopAntennaSn != null && bs11LoopAntennaSn.Count > 0 && bs11LoopAntennaSn.Contains(SerialNumber))
          {
            ProductFamily = ProductFamily.Bs11LoopAntenna;
            ProductType = ProductType.Bs11LoopAntenna;
            ProductInterface = ProductInterface.UsbHid; // ProductInterface.UsbVcp;
            str1 = "BS11 Loop Antenna";
            break;
          }
          break;
        case ProductFamily.SiGsmDn:
          str1 = "SI-GSMDN";
          str2 = str3 = "";
          ProductType = ProductType.SiGsmDn;
          ProductInterface = ProductInterface.UsbHid; // ProductInterface.UsbVcp;
          break;
        case ProductFamily.Bsf9:
        case ProductFamily.Bsm9:
          ProductType = ProductFamily == ProductFamily.Bsf9 ? ProductType.Bsf9 : ProductType.Bsm9;
          BoardVersion = (byte)9;
          ProductInterface = ProductInterface.UsbHid; // ProductInterface.UsbVcp;
          str1 = "BS" + (ProductType == ProductType.Bsm9 ? "M" : "F") + " 9";
          break;
        case ProductFamily.SiPoint:
          ProductInterface = ProductInterface.UsbVcp;
          switch (cfg1)
          {
            case 144:
              ProductType = ProductType.SiPointGolf;
              str1 = "SI-Point Golfbox";
              break;
            case 146:
              ProductType = ProductType.SiPointSportident;
              str1 = "SI-Point SPORTident";
              break;
            default:
              str1 = "SI-Point";
              break;
          }
          break;
      }
      var hasBattery = false;
      switch (ProductType)
      {
        case ProductType.SimSrr:
        case ProductType.Bsm8:
          hasBattery = false;
          break;
        default:
          hasBattery = true;
          break;
      }

      return new SportidentProduct(code, SerialNumber, str1 + str2 + str3 + str4, ProductType, ProductFamily, ProductInterface, ProductConfiguration, BoardVersion, channel, productDate, hasBattery, firmware);
    }

    private static string GetUartType(byte cfg2, byte uart)
    {
      if (uart == (byte)1)
      {
        if (((int)cfg2 & 56) == 48)
          return " (USB)";
        return ((int)cfg2 & 56) != 40 ? string.Empty : " (RS232)";
      }
      if (((int)cfg2 & 7) == 6)
        return " (USB)";
      return ((int)cfg2 & 7) != 5 ? string.Empty : " (RS232)";
    }
  }

  public record SportidentProduct(
    int Code,
    string SerialNumber,
    string ProductString,
    ProductType ProductType,
    ProductFamily ProductFamily,
    ProductInterface ProductInterface,
    ushort ProductConfiguration,
    byte BoardVersion,
    SrrChannel Channel,
    DateTime ProductionDate,
    bool hasBattery,
    int firmware);

  public enum ProductInterface
  {
    NotSet,
    Rs232,
    UsbVcp,
    UsbHid,
  }

  public enum SrrChannel
  {
    Red = 0,
    Blue = 1
  }
  public enum ProductFamily
  {
    NotSet = 0,
    SimSrr = 33, // 0x00000021
    SiPoint2 = 114, // 0x00000072
    Bs8SiMaster = 136, // 0x00000088
    [Obsolete] Bs10UfoReaderSiGolf = 138, // 0x0000008A
    [Obsolete] Bs10UfoReaderSportIdent = 139, // 0x0000008B
    Bsx8Ostarter = 144, // 0x00000090
    [Obsolete] Bsx4 = 148, // 0x00000094
    [Obsolete] Bsx6 = 150, // 0x00000096
    Bsx7 = 151, // 0x00000097
    Bsx8 = 152, // 0x00000098
    Bs11LoopAntenna = 153, // 0x00000099
    Bs11Large = 154, // 0x0000009A
    Bs11Small = 155, // 0x0000009B
    [Obsolete] Bs12GsmUart = 156, // 0x0000009C
    SiGsmDn = 157, // 0x0000009D
    Bsf9 = 158, // 0x0000009E
    Bsm9 = 159, // 0x0000009F
    SiPoint = 241, // 0x000000F1
  }

  public enum ProductType
  {
    NotSet = 0,
    SimSrr = 33, // 0x00000021
    [Obsolete] Bsx4 = 148, // 0x00000094
    [Obsolete] Bsx6 = 150, // 0x00000096
    [Obsolete] Bs12GsmUart = 6556, // 0x0000199C
    SiGsmDn = 7069, // 0x00001B9D
    Bs8SiMaster = 33160, // 0x00008188
    Bsf8Ostarter = 33168, // 0x00008190
    Bsf7 = 33175, // 0x00008197
    Bsf8 = 33176, // 0x00008198
    Bsf9 = 33182, // 0x0000819E
    [Obsolete] Bs10UfoReaderSiGolf = 35210, // 0x0000898A
    [Obsolete] Bs10UfoReaderSportIdent = 35211, // 0x0000898B
    Bs11LoopAntenna = 36249, // 0x00008D99
    SiPointGolf2 = 36978, // 0x00009072
    SiPointGolf = 37105, // 0x000090F1
    Bsm7 = 37271, // 0x00009197
    Bsm8 = 37272, // 0x00009198
    Bsm9 = 37279, // 0x0000919F
    SiPointSportident = 37617, // 0x000092F1
    Bs7S = 38295, // 0x00009597
    Bs11Large = 40346, // 0x00009D9A
    Bs7P = 45463, // 0x0000B197
    Bs11Small = 52635, // 0x0000CD9B
  }
}
