using RadioSender.Helpers;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Target.OResults;
using System;
using System.ComponentModel.DataAnnotations;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Flow.Modules;

public enum HttpVerb { GET, POST, PUT, DELETE }
public sealed record HttpSettings
{
  [Required, Display(Name = "URL", Description = "Event placeholders are supported, for example {CompetitorId}, {Control}, {Status}, {Cancellation}.")]
  public string Url { get; init; } = "http://127.0.0.1:8080/punch?card={CompetitorId}&control={Control}&time={Time:HH:mm:ss.fff}";
  public HttpVerb Method { get; init; } = HttpVerb.GET;
}
public sealed record OribosTargetSettings
{
  [Required, Url, Display(Name = "Oribos server")]
  public string Host { get; init; } = "http://127.0.0.1:8080";
}

// Delivery belongs to TargetQueue. No global jobs retain old destinations after Apply.
public sealed class HttpDeliveryModule(Func<Punch, HttpRequestMessage?> request,
  bool oribosResponse = false) : FlowModule
{
  private readonly HttpClient _client = new() { Timeout = Timeout.InfiniteTimeSpan, MaxResponseContentBufferSize = 1024 * 1024 };
  public override async ValueTask<DeliveryResult> SendAsync(Punch punch, CancellationToken ct)
  {
    using var message = request(punch);
    if (message == null) return new("Suppressed", "This protocol cannot represent the event or identifier.");
    using var response = await _client.SendAsync(message, ct);
    if (!response.IsSuccessStatusCode) return new("Failed", $"HTTP {(int)response.StatusCode}.");
    if (oribosResponse && !(await response.Content.ReadAsStringAsync(ct)).Contains("Ok", StringComparison.Ordinal))
      return new("Failed", "Oribos did not acknowledge the event.");
    return new("Accepted", $"HTTP {(int)response.StatusCode}.");
  }
  public override ValueTask DisposeAsync() { _client.Dispose(); return ValueTask.CompletedTask; }

  public static HttpDeliveryModule Http(HttpSettings settings) => new(punch =>
  {
    if (punch.Cancellation && !FormatStringHelper.UsesPlaceholder(settings.Url, "Cancellation") ||
        punch.CompetitorStatus != CompetitorStatus.Unknown && !FormatStringHelper.UsesPlaceholder(settings.Url, "Status")) return null;
    return new(new HttpMethod(settings.Method.ToString()), FormatStringHelper.GetString(punch, settings.Url));
  });
  public static HttpDeliveryModule Oribos(OribosTargetSettings settings) => new(punch =>
  {
    var path = Hosts.Target.Oribos.OribosService.BuildPunchPath(punch);
    return path == null ? null : new(HttpMethod.Get, new Uri(new Uri(settings.Host), path));
  }, true);
  public static HttpDeliveryModule OResults(OResultsConfiguration settings) => new(punch =>
  {
    if (punch.CompetitorStatus != CompetitorStatus.Unknown) return null;
    var record = OResultsService.Encode(punch, settings.UseUtc, settings.IgnoreCompetitorIdType);
    return record == null ? null : new(HttpMethod.Post, new Uri(new Uri(settings.Host), settings.Path))
    { Content = JsonContent.Create(new OResultsRequest(settings.ApiKey!, [record])) };
  });
}
