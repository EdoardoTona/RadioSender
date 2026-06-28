using RadioSender.Hosts.Common;

namespace RadioSender.Hosts.Enrichment
{
  // An enrichment source augments punches with competitor data (e.g. Card↔Bib mapping,
  // name, class, theoretical start time) without generating punches itself.
  // It keeps an in-memory lookup table refreshed in the background; Enrich must be
  // non-blocking (a simple lookup).
  public interface IEnrichmentSource
  {
    // Name used to activate this enricher from a filter's Enrichers list.
    string Name { get; }

    // Returns the punch enriched (populating Punch.Competitor), or the same punch
    // unchanged when there is no match (best-effort).
    Punch Enrich(Punch punch);
  }
}
