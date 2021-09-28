using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Liveresults.Converters
{
  public class StringToBoolConverter : JsonConverter<bool>
  {
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
      return bool.Parse(reader.GetString()!);
    }

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
    {
      writer.WriteStringValue(value.ToString().ToLowerInvariant());
    }
  }
}
