using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Collections.Generic;

namespace RadioSender.Hosts.Common.Filters
{
  public static class ConfigureFilters
  {
    public static IHostBuilder UseFilters(this IHostBuilder builder)
    {
      builder.ConfigureServices((context, services) =>
      {
        services.Configure<FiltersConfiguration>(context.Configuration.GetSection("Filters"));

        // // TODO remove:
        var filters = context.Configuration.GetSection("Filters").Get<FiltersConfiguration>();

        if (filters != null)
        {
          foreach (var filter in filters)
          {
            services.AddSingleton<IFilter>(filter);
          }
        }

      });

      return builder;
    }
  }
}
