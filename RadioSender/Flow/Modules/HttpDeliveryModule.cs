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
  private const int MaxAttempts = 4;
  private readonly HttpClient _client = new() { Timeout = Timeout.InfiniteTimeSpan, MaxResponseContentBufferSize = 1024 * 1024 };
  public override async ValueTask<DeliveryResult> SendAsync(Punch punch, CancellationToken ct)
  {
    for (var attempt = 1; ; attempt++)
    {
      ct.ThrowIfCancellationRequested();
      var delay = TimeSpan.FromMilliseconds(250 * (1 << (attempt - 1)));
      string failure;
      try
      {
        // Requests and their content can only be sent once; recreate both for each attempt.
        using var message = request(punch);
        if (message == null) return new("Suppressed", "This protocol cannot represent the event or identifier.");
        using var response = await _client.SendAsync(message, ct);
        var status = (int)response.StatusCode;
        if (response.IsSuccessStatusCode)
        {
          if (oribosResponse && !(await response.Content.ReadAsStringAsync(ct)).Contains("Ok", StringComparison.Ordinal))
            return new("Failed", "Oribos did not acknowledge the event.");
          return new("Accepted", $"HTTP {status}. Completed after {Attempts(attempt)}.");
        }
        failure = $"HTTP {status}";
        if (status is not (408 or 429) && status < 500)
          return Failed(failure, attempt);

        // Respect server throttling without extending the target queue's delivery deadline.
        var retryAfter = response.Headers.RetryAfter;
        var requestedDelay = retryAfter?.Delta ?? retryAfter?.Date - DateTimeOffset.UtcNow;
        if (requestedDelay > delay) delay = requestedDelay.Value;
      }
      catch (HttpRequestException e)
      {
        failure = $"HTTP transport error ({e.HttpRequestError})";
      }
      if (attempt == MaxAttempts) return Failed(failure, attempt);
      Logger.Warning("HTTP delivery attempt {Attempt}/{MaxAttempts} failed ({Reason}); retrying in {DelayMs} ms.",
        attempt, MaxAttempts, failure, delay.TotalMilliseconds);
      await Task.Delay(delay, ct);
    }
  }
  private static string Attempts(int count) => $"{count} attempt{(count == 1 ? "" : "s")}";
  private static DeliveryResult Failed(string reason, int attempts) => new("Failed", $"{reason}. Failed after {Attempts(attempts)}.");
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
