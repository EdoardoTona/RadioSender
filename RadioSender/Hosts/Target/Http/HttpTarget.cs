using Hangfire;
using RadioSender.Helpers;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Target.Http;

public class HttpTarget : ITarget
{

  private readonly FilterService _filterService;
  private readonly IBackgroundJobClient _backgroundJobClient;
  private HttpTargetConfiguration _configuration;
  private static IHttpClientFactory? _httpClientFactory;

  public HttpTarget(
    FilterService filterService,
    IHttpClientFactory httpClientFactory,
    IBackgroundJobClient backgroundJobClient,
    HttpTargetConfiguration configuration)
  {
    _configuration = configuration;
    _httpClientFactory = httpClientFactory;
    _backgroundJobClient = backgroundJobClient;
    _filterService = filterService;
  }
  public Task SendDispatches(IEnumerable<PunchDispatch> dispatches, CancellationToken ct = default)
  {
    foreach (var dispatch in dispatches)
      SendDispatch(dispatch, ct);

    return Task.CompletedTask;
  }
  public Task SendDispatch(PunchDispatch dispatch, CancellationToken ct = default)
  {
    if (dispatch.Punches == null)
      return Task.CompletedTask;

    var punches = _filterService.Transform(_configuration.Filter, dispatch.Punches);

    var sendsCancellation = !string.IsNullOrEmpty(_configuration.Url) && FormatStringHelper.UsesPlaceholder(_configuration.Url, "Cancellation");
    var sendsStatus = !string.IsNullOrEmpty(_configuration.Url) && FormatStringHelper.UsesPlaceholder(_configuration.Url, "Status");

    foreach (var punch in punches)
    {
      // The receiver has no way to tell a cancellation/status change apart from a normal
      // punch unless the URL carries it explicitly, so skip what it can't represent.
      if (punch.Cancellation && !sendsCancellation)
        continue;
      if (punch.CompetitorStatus != CompetitorStatus.Unknown && !sendsStatus)
        continue;

      _backgroundJobClient.Enqueue(() => SendPunchAction(_configuration, punch, default));
    }
    return Task.CompletedTask;
  }


  public static async Task SendPunchAction(HttpTargetConfiguration _configuration, Punch punch, CancellationToken ct = default)
  {
    if (string.IsNullOrEmpty(_configuration.Url) || _httpClientFactory == null)
      throw new ArgumentException("Missing url");

    using var httpClient = _httpClientFactory.CreateClient();

    var url = _configuration.Url.Contains("localhost") ? _configuration.Url.Replace("localhost", "127.0.0.1") : _configuration.Url; // optimization to skip the dns resolution

    url = FormatStringHelper.GetString(punch, url);

    var method = _configuration.Method?.ToLowerInvariant();
    var request = new HttpRequestMessage
    {
      RequestUri = new Uri(url),
      Method = method switch
      {
        "get" => HttpMethod.Get,
        "post" => HttpMethod.Post,
        "delete" => HttpMethod.Delete,
        "put" => HttpMethod.Put,
        "patch" => HttpMethod.Patch,
        "head" => HttpMethod.Head,
        "options" => HttpMethod.Options,
        "trace" => HttpMethod.Trace,
        _ => HttpMethod.Get
      }
    };

    var response = await httpClient.SendAsync(request, ct);

    if (_configuration.EnsureSuccessStatusCode)
      response.EnsureSuccessStatusCode();

  }

}
