using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using RadioSender.Configuration;
using RadioSender.Flow;
using RadioSender.Flow.Modules;
using RadioSender.Hubs;
using RadioSender.Hosts.Common;
using RadioSender.Runtime;
using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Api;

public static class FlowApi
{
  public sealed record OpenRequest(string Path);
  public sealed record SaveRequest(long Revision, FlowDocument? Document, string? Path = null);
  public sealed record ApplyRequest(Guid DocumentId, long Revision);
  public sealed record ManualRequest(Guid SessionId, long Revision, Punch Punch);

  public static void AddFlowServices(this IServiceCollection services)
  {
    services.AddSingleton(ModuleRegistry.CreateDefault());
    services.AddSingleton<FlowValidator>();
    services.AddSingleton<FlowDocuments>();
    services.AddSingleton<FlowJournal>();
    services.AddSingleton<FlowRuntime>();
    services.AddHostedService(sp => sp.GetRequiredService<FlowRuntime>());
    services.AddHostedService<FlowNotifications>();
    services.ConfigureHttpJsonOptions(o =>
    {
      o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
      o.SerializerOptions.MaxDepth = 32;
    });
  }

  public static void MapFlowApi(this WebApplication app)
  {
    app.Use(async (context, next) =>
    {
      if (!context.Request.Path.StartsWithSegments("/api/flow") && !context.Request.Path.StartsWithSegments("/flowHub"))
      { await next(context); return; }
      var remote = context.Connection.RemoteIpAddress;
      var host = context.Request.Host.Host;
      var localHost = host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip);
      var origin = context.Request.Headers.Origin.ToString();
      var sameOrigin = string.IsNullOrEmpty(origin) || Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
        uri.Authority.Equals(context.Request.Host.Value, StringComparison.OrdinalIgnoreCase) && uri.Scheme == context.Request.Scheme;
      if (remote == null || !IPAddress.IsLoopback(remote) || !localHost || !sameOrigin)
      { context.Response.StatusCode = 403; await context.Response.WriteAsJsonAsync(new { error = "Flow editing is available on the local RadioSender host only." }); return; }
      if (context.Request.Path.StartsWithSegments("/api/flow") && !HttpMethods.IsGet(context.Request.Method) &&
          context.Request.Headers["X-RadioSender-Client"] != "flow-editor")
      { context.Response.StatusCode = 403; return; }
      try { await next(context); }
      catch (Exception e) when (e is FlowException or JsonException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
      {
        if (context.Response.HasStarted) throw;
        context.Response.StatusCode = e is FlowException f ? f.Status : 400;
        await context.Response.WriteAsJsonAsync(new { error = e.Message });
      }
    });

    var api = app.MapGroup("/api/flow");
    api.MapGet("/modules", (ModuleRegistry registry) => registry.Catalog);
    api.MapGet("/defaults", () => new { directory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) });
    api.MapPost("/documents/new", (OpenRequest request, FlowDocuments documents, CancellationToken ct) => documents.OpenAsync(request.Path, true, ct));
    api.MapPost("/documents/open", (OpenRequest request, FlowDocuments documents, CancellationToken ct) => documents.OpenAsync(request.Path, false, ct));
    api.MapGet("/documents/{id:guid}", (Guid id, FlowDocuments documents, CancellationToken ct) => documents.ReadAsync(id, ct));
    api.MapPut("/documents/{id:guid}", (Guid id, SaveRequest request, FlowDocuments documents, CancellationToken ct) => documents.SaveAsync(id, request.Revision, request.Document, ct: ct));
    api.MapPost("/documents/{id:guid}/save", (Guid id, SaveRequest request, FlowDocuments documents, CancellationToken ct) => documents.SaveAsync(id, request.Revision, ct: ct));
    api.MapPost("/documents/{id:guid}/save-as", (Guid id, SaveRequest request, FlowDocuments documents, CancellationToken ct) => documents.SaveAsync(id, request.Revision, request.Document, request.Path ?? throw new FlowException("Choose a file path."), ct));
    api.MapPost("/validate", (FlowDocument document, FlowValidator validator) => validator.Validate(document));
    api.MapGet("/runtime", (FlowRuntime runtime) => runtime.Snapshot());
    api.MapPost("/runtime/apply", async (ApplyRequest request, FlowDocuments documents, FlowRuntime runtime, CancellationToken ct) =>
    {
      var snapshot = await documents.SaveAsync(request.DocumentId, request.Revision, ct: ct);
      return await runtime.ApplyAsync(snapshot.Id, snapshot.Path, snapshot.Revision, snapshot.Document, ct);
    });
    api.MapPost("/runtime/stop", async (FlowRuntime runtime, CancellationToken ct) => { await runtime.StopFlowAsync(ct); return Results.Ok(); });
    api.MapGet("/nodes/{id}/observations", (string id, string direction, long? after, FlowJournal journal) => journal.Read(id, direction, after ?? 0));
    api.MapPost("/nodes/{id}/replay", (string id, ReplayRequest request, FlowRuntime runtime, CancellationToken ct) => runtime.ReplayAsync(id, request, ct: ct));
    api.MapPost("/nodes/{id}/retry", (string id, ReplayRequest request, FlowRuntime runtime, CancellationToken ct) => runtime.ReplayAsync(id, request, true, ct));
    api.MapPost("/nodes/{id}/commands/send", async (string id, ManualRequest request, FlowRuntime runtime, CancellationToken ct) =>
    {
      await runtime.SendManualAsync(request.SessionId, request.Revision, id, request.Punch, ct);
      return Results.Ok(new { status = "Accepted" });
    });
    app.MapHub<FlowHub>("/flowHub");
  }
}
