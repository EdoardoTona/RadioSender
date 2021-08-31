using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RadioSender.Hubs;
using System.Text.Json.Serialization;

namespace RadioSender
{
  public class Startup
  {
    public void ConfigureServices(IServiceCollection services)
    {
      services.AddRazorPages();

      services.AddSignalR()
              .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

      services.AddSingleton<HubEvents>();
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
          context.Context.Response.Headers.Add("Cache-Control", "no-cache");
        }
      });

      app.UseHangfireDashboard();

      app.UseRouting();

      app.UseEndpoints(endpoints =>
      {
        endpoints.MapRazorPages();

        endpoints.MapHub<DeviceHub>("/deviceHub");

        endpoints.MapHangfireDashboard();

      });

    }

  }
}
