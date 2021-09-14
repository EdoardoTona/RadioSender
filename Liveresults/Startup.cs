using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Text.Json.Serialization;

namespace Liveresults
{
  public class Startup
  {
    public void ConfigureServices(IServiceCollection services)
    {
      services.AddHealthChecks();

      services.AddRazorPages();

      services.AddSignalR()
              .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));


    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
      if (env.IsDevelopment())
      {
        app.UseDeveloperExceptionPage();
      }

      app.UseStaticFiles(new StaticFileOptions
      {
        OnPrepareResponse = context =>
        {
          if (env.IsDevelopment())
            context.Context.Response.Headers.Add("Cache-Control", "no-cache");
          else
            context.Context.Response.Headers.Add("Cache-Control", "private, max-age=86400"); // 1 day
        }
      });



      app.UseRouting();

      app.UseEndpoints(endpoints =>
      {
        endpoints.MapHealthChecks("healthz");

        endpoints.MapRazorPages();

      });


    }

  }
}
