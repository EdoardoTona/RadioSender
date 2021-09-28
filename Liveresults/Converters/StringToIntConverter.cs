using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Liveresults.Converters
{
  public class StringToIntConverter : JsonConverter<int>
  {
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
      return int.Parse(reader.GetString()!);
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
    {
      writer.WriteStringValue(value.ToString());
    }
  }
}
