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

  private readonly IBackgroundJobClient _backgroundJobClient;
  private IFilter _filter = Filter.Invariant;
  private HttpTargetConfiguration _configuration;
  private static IHttpClientFactory? _httpClientFactory;

  public HttpTarget(
    IEnumerable<IFilter> filters,
    IHttpClientFactory httpClientFactory,
    IBackgroundJobClient backgroundJobClient,
    HttpTargetConfiguration configuration)
  {
    _configuration = configuration;
    _httpClientFactory = httpClientFactory;
    UpdateConfiguration(filters, configuration);
    _backgroundJobClient = backgroundJobClient;
  }
  public void UpdateConfiguration(IEnumerable<IFilter> filters, Configuration configuration)
  {
    Interlocked.Exchange(ref _configuration!, configuration as HttpTargetConfiguration);
    Interlocked.Exchange(ref _filter, filters.GetFilter(_configuration.Filter));
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

    var punches = _filter.Transform(dispatch.Punches);

    foreach (var punch in punches)
    {
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
