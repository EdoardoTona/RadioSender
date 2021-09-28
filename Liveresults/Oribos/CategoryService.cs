using Liveresults.Hubs;
using Liveresults.Models;
using Microsoft.AspNetCore.SignalR;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Liveresults.Oribos
{
  public class CategoryService : IDisposable
  {

    private readonly IHubContext<ResultsHub, IResultsHub> _hubContext;
    private readonly HubEvents _hubEvents;

    private IEnumerable<Category> _categories = Array.Empty<Category>();

    public CategoryService(
      IHubContext<ResultsHub, IResultsHub> hubContext,
      HubEvents hubEvents)
    {
      _hubEvents = hubEvents;
      _hubEvents.GroupJoined += HubEvents_GroupJoined;

      _hubContext = hubContext;
    }


    private async void HubEvents_GroupJoined(HubCallerContext context, string group)
    {
      try
      {
        if (_hubContext == null) return;

        if (group == ResultsHub.GROUP_CATEGORIES)
        {
          await _hubContext.Clients.Client(context.ConnectionId).UpdateCategories(_categories);
          await _hubContext.Groups.AddToGroupAsync(context.ConnectionId, ResultsHub.GROUP_CATEGORIES);
        }
      }
      catch (OperationCanceledException)
      {
        // quiet
      }
      catch (Exception e)
      {
        Log.Error(e, "Exception Group Join");
      }
    }

    public void Push(FullDataDto? fullDataDto)
    {

      var categories = GetCategoriesFromFullData(fullDataDto);

      var differences = false;
      foreach (var c in categories)
      {
        var old = _categories.Where(i => i.ShortName == c.ShortName).FirstOrDefault();

        if (c != old)
        {
          differences = true;
          break;
        }
      }

      if (differences)
      {
        _categories = categories;
        _hubContext.Clients.Group(ResultsHub.GROUP_CATEGORIES).UpdateCategories(_categories);
      }
    }

    public void Dispose()
    {
      _hubEvents.GroupJoined -= HubEvents_GroupJoined;
    }

    public static IEnumerable<Category> GetCategoriesFromFullData(FullDataDto? fullDataDto)
    {
      return fullDataDto.Classes.SelectMany(c =>
      {

        if (fullDataDto.Race.Type != "Staffetta" || c.Leg == 1)
        {
          return new[]{new Category()
          {
            Name = c.Name,
            ShortName = c.ShortName,
            Intermediates = c.Radiopoints.Select(r => new Intermediate() { Code = r.Code, Name = !string.IsNullOrWhiteSpace(r.Description) ? r.Description : r.Code.ToString() }),
            Leg = 1,
            Legs=1
          }};
        }
        else
        {
          var classes = new Category[c.Leg];
          for (int i = 1; i <= c.Leg; i++)
          {
            classes[i - 1] = new Category()
            {
              Name = c.Name,
              ShortName = c.ShortName + "-" + i,
              Intermediates = c.Radiopoints.Select(r => new Intermediate() { Code = r.Code, Name = !string.IsNullOrWhiteSpace(r.Description) ? r.Description : r.Code.ToString() }),
              Leg = i,
              Legs = c.Leg
            };
          }
          return classes;
        }


      }
      );
    }
  }
}
