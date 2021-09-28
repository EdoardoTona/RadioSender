using Liveresults.Converters;
using System.Text.Json.Serialization;

namespace Liveresults.Models
{
  public record LastUpdateDto
  {

    public string Update { get; set; }

    [JsonConverter(typeof(StringToBoolConverter))]
    public bool Chat { get; set; }
  }
}
