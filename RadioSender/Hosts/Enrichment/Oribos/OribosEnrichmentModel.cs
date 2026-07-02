using System;
using System.Collections.Generic;

namespace RadioSender.Hosts.Enrichment.Oribos
{
  // Subset of Oribos ORServer.lastupdate.jsp / ORServer.fullweb.jsp responses.
  // Only the fields needed for enrichment + status change detection are mapped.
  // JSON is camelCase (see OribosService JsonSerializerOptions).

  public record OrServerUpdate
  {
    public string? Update { get; init; }
  }

  public record OrServer
  {
    public DateTimeOffset Update { get; init; }
    public OrRace Race { get; init; } = new();
    public IEnumerable<OrCompetitor>? Competitors { get; init; }
    public IEnumerable<OrClub>? Clubs { get; init; }
  }

  public record OrClub
  {
    public string? CountryId { get; init; } // matches OrCompetitor.ClubId
    public string? Country { get; init; }
    public string? Name { get; init; }
    public string? ShortName { get; init; }
  }

  public record OrRace
  {
    public DateTimeOffset Startutc { get; init; }
    public Guid Guid { get; init; }
    public Guid? Mainguid { get; init; }
    public string? Timezone { get; init; }
  }

  public record OrCompetitor
  {
    public int? Card { get; init; }
    public int? Card2 { get; init; }
    public int? Bib { get; init; }
    public string? Name { get; init; }
    public string? Surname { get; init; }
    public string? Class { get; init; }
    public string? ClubId { get; init; }      // matches OrClub.CountryId
    public string? ClubCountry { get; init; } // club nation
    public string? Naz { get; init; }         // athlete nation
    public double Start { get; init; } // seconds relative to race start
    public double Finish { get; init; } // seconds relative to race start, 0 = no finish time
    public string? Status { get; init; } // CL, PE/PM, NP, SQ, RI, FT, GA, IP, DI...
    public bool Sj { get; init; } // sub judice: result awaiting confirmation (auto check or manual review)
  }
}
