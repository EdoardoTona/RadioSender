using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using TimeZoneConverter;

namespace Liveresults.Converters
{
  public class WinTimeZoneToIanaConverter : JsonConverter<string>
  {
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
      return TZConvert.WindowsToIana(reader.GetString());
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
      writer.WriteStringValue(TZConvert.IanaToWindows(value));
    }
  }
}