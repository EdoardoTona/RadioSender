using System;
using System.Collections.Generic;

namespace RadioSender.Hosts.Common
{
  public abstract record GraphElement(string? Name, long? LatencyMs, int? SignalStength);
  public record NodeNew(string Id, string? Name, long? LatencyMs, int? SignalStength) : GraphElement(Name, LatencyMs, SignalStength)
  {
    public static readonly NodeNew Localhost = new(Guid.Empty.ToString(), "localhost", 0, 1);
  }
  public record Hop(string From, string To, long? LatencyMs, int? SignalStength) : GraphElement(null, LatencyMs, SignalStength)
  {
    public string Id { get => From + To; }
  }
  public record PunchDispatch(IEnumerable<Punch>? Punches = null, IEnumerable<Hop>? Hops = null, IEnumerable<NodeNew>? Nodes = null);
  // Dati anagrafici arricchiti da un IEnrichmentSource (es. mappatura Card↔Bib da Oribos).
  // Tutti i campi opzionali: ciò che l'enricher non conosce resta null.
  public record Competitor(
    string? Bib = null,
    string? Card = null,
    string? Card2 = null,       // seconda card assegnata all'atleta, se presente
    string? Name = null,
    string? Class = null,
    string? Nation = null,      // nazione dell'atleta
    string? ClubId = null,
    string? ClubName = null,
    string? ClubNation = null,  // nazione del club
    DateTime? StartTime = null  // orario di partenza teorico (assoluto)
    );

  public record Punch(
    string CompetitorId,
    DateTime Time,
    int Control,
    string SourceId,
    DateTimeOffset ReceivedAt,
    CompetitorIdType CompetitorIdType = CompetitorIdType.Unknown,
    PunchControlType ControlType = PunchControlType.Unknown,
    CompetitorStatus CompetitorStatus = CompetitorStatus.Unknown,
    bool Cancellation = false,
    bool NetTime = false,
    Competitor? Competitor = null
    )
  {
    public string Card => CompetitorId;
    public string ComparisonKey => $"{CompetitorIdType}-{CompetitorId}-{ControlType}-{Control}-{Time:O}-{SourceId}-{CompetitorStatus}-{Cancellation}-{NetTime}";
    // Same identity as ComparisonKey but always non-cancelled: the key of the punch a Cancellation=true event refers to.
    public string UncancelledComparisonKey => $"{CompetitorIdType}-{CompetitorId}-{ControlType}-{Control}-{Time:O}-{SourceId}-{CompetitorStatus}-False-{NetTime}";
    public string? ControlTypeShort => ControlType switch
    {
      PunchControlType.Control => "CN",
      PunchControlType.Finish => "FIN",
      PunchControlType.Clear => "CLR",
      PunchControlType.Check => "CHK",
      PunchControlType.Start => "STA",
      _ => null,
    };
  }
  public enum CompetitorIdType
  {
    Unknown = 0,
    BibNumber,
    TimingTransponder,
    PunchingCard
  }
  public enum PunchControlType
  {
    Unknown = 0,
    Control,
    Finish,
    Clear,
    Check,
    Start
  }
  public enum CompetitorStatus
  {
    Unknown = 0,
    OK,
    DNS,
    DNF,
    MP,
    DSQ,
    OverTime,
    WaitingStart,
    Running
  }
}
