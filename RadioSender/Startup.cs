using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RadioSender.Hubs;
using RadioSender.Hubs.Devices;

namespace RadioSender
{
  public class Startup
  {
    public void ConfigureServices(IServiceCollection services)
    {
      services.AddRazorPages();

      services.AddSignalR();

      services.AddSingleton<DeviceService>();


    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
      if (env.IsDevelopment())
      {
        app.UseDeveloperExceptionPage();
      }

      //app.UseDefaultFiles();
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
        //endpoints.MapHub<PunchHub>("/punchHub");
        endpoints.MapHub<DeviceHub>("/deviceHub");

        endpoints.MapHangfireDashboard();

      });

    }

  }
}
