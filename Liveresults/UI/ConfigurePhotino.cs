using Liveresults;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RadioSender.UI;

namespace Microsoft.Extensions.Hosting
{
  public static class ConfigurePhotino
  {
    public static IHostBuilder ActivatePhotino(this IHostBuilder builder)
    {

      builder
        .ConfigureServices((context, services) =>
        {

          var urls = context.Configuration.GetSection("Urls").Get<string>();

          services.AddHostedService(sp => new PhotinoHostedService(
            urls,
            sp.GetRequiredService<IHostApplicationLifetime>(),
            sp.GetRequiredService<IHostEnvironment>()
            ));

        })
        .ConfigureWebHostDefaults(webBuilder => webBuilder.UseStartup<Startup>());

      return builder;

    }
  }
}
