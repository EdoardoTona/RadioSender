using Liveresults.Converters;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Liveresults.Models
{
  public record FullDataDto
  {
    public string Update { get; set; }
    public RaceDto Race { get; set; }
    public IEnumerable<CourseDto> Courses { get; set; }
    public IEnumerable<ClubDto> Clubs { get; set; }
    public IEnumerable<ClassDto> Classes { get; set; }

    public IEnumerable<CompetitorDto> Competitors { get; set; }
    public IEnumerable<RelayDto> Relays { get; set; }
  }

  public record RaceDto
  {
    public string Guid { get; set; }
    public string MainGuid { get; set; }
    [JsonConverter(typeof(StringToBoolConverter))]
    public bool IsOpen { get; set; }
    [JsonConverter(typeof(WinTimeZoneToIanaConverter))]
    public string Timezone { get; set; }
    [JsonPropertyName("startutc")]
    public DateTimeOffset Start { get; set; }
    [JsonPropertyName("desc")]
    public string Name { get; set; }
    public string Team { get; set; }
    public string Date { get; set; }
    public string Type { get; set; }
    public string GridMode { get; set; }

    [JsonConverter(typeof(StringToBoolConverter))]
    public bool OneManRelay { get; set; }
    public string PunchMode { get; set; }
    public string StartMode { get; set; }
    public string FinishMode { get; set; }
    public string RaceStart { get; set; }
    public string TimeMax { get; set; }
    public string Timestamp { get; set; }
    public string Version { get; set; }
    public string License { get; set; }
    [JsonConverter(typeof(StringToIntConverter))]
    public int RaceNumber { get; set; }
    [JsonPropertyName("ismd")]
    public bool IsMultidays { get; set; }
  }

  public record CourseDto
  {
    public string Name { get; set; }
    public int Length { get; set; }
    public int Climbing { get; set; }
    public int EntriesMax { get; set; }
    public IEnumerable<ControlPointDto> Controls { get; set; }
  }
  public record ControlPointDto
  {
    public int Code { get; set; }
    public int Score { get; set; }
  }
  public record ClubDto
  {
    public string Country { get; set; }
    public string CountryId { get; set; }
    public string Name { get; set; }
    public string ShortName { get; set; }

  }
  public record ClassDto
  {

    public string Name { get; set; }
    public string ShortName { get; set; }
    public int RaceStart { get; set; }
    public int EntriesMax { get; set; }
    public IEnumerable<string> Courses { get; set; }
    public double Fee { get; set; }
    public int Start { get; set; }
    public bool FreeStart { get; set; }
    public bool Ludica { get; set; }
    public bool Ago { get; set; }
    public string Sex { get; set; }
    public int AgeMin { get; set; }
    public int AgeMax { get; set; }
    public int Leg { get; set; }
    public IEnumerable<RadioControlDto> Radiopoints { get; set; }
  }

  public record RadioControlDto
  {
    public int Code { get; set; }
    public int CodeX { get; set; }
    [JsonPropertyName("desc")]
    public string? Description { get; set; }
  }


  public record CompetitorDto
  {
    public string Class { get; set; }
    public int Id { get; set; }
    public string IdWre { get; set; }
    public string Birth { get; set; }
    public int Card { get; set; }
    public int Card2 { get; set; }
    public double Check { get; set; }
    public double Start { get; set; }
    public double Finish { get; set; }
    public string Readout { get; set; }
    public double Time { get; set; }
    public bool SJ { get; set; }
    public string Status { get; set; }
    public double Score { get; set; }
    public string Pos { get; set; }
    public string Surname { get; set; }
    public string Name { get; set; }
    public int Bib { get; set; }
    public string Naz { get; set; }
    public string ClubCountry { get; set; }
    public string ClubId { get; set; }
    public bool RentCard { get; set; }
    public string CardOrg { get; set; }
    public string Sex { get; set; }
    public bool Ago { get; set; }
    public string Note { get; set; }
    public string NoteSq { get; set; }
    public double BonusTime { get; set; }
    public int Leg { get; set; }
    public IEnumerable<ControlPointPassageDto> Radio { get; set; }
    public IEnumerable<ControlPointPassageDto> Intermediates { get; set; }
  }

  public record ControlPointPassageDto
  {
    public int Point { get; set; }
    public double Time { get; set; }
  }

  public record RelayDto
  {

  }


}