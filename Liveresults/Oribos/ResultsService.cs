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
  public class ResultsService
  {
    private readonly IHubContext<ResultsHub, IResultsHub> _hubContext;
    private readonly HubEvents _hubEvents;

    private IEnumerable<Category> _categories;
    private Dictionary<string, List<ResultItem>> _results = new();
    private Dictionary<string, string> _connectionIdsGroups = new();

    private readonly TimeSpan lastUpdateInterval = TimeSpan.FromSeconds(30);

    public ResultsService(
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

        if (group.StartsWith(ResultsHub.GROUP_RESULTS))
        {
          var requestedCategory = group.Replace(ResultsHub.GROUP_RESULTS, "");

          if (_connectionIdsGroups.ContainsKey(context.ConnectionId))
          {
            await _hubContext.Groups.RemoveFromGroupAsync(context.ConnectionId, _connectionIdsGroups[context.ConnectionId]);
          }


          if (_results.ContainsKey(requestedCategory))
          {
            await _hubContext.Clients.Client(context.ConnectionId).UpdateResult(_results[requestedCategory]);
          }

          _connectionIdsGroups[context.ConnectionId] = group;
          await _hubContext.Groups.AddToGroupAsync(context.ConnectionId, group);
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
      try
      {
        _categories = CategoryService.GetCategoriesFromFullData(fullDataDto);

        var firstStart = fullDataDto.Race.Start;

        _results = new();

        foreach (var category in _categories)
        {
          _results[category.ShortName] = new();
        }

        foreach (var c in fullDataDto.Competitors)
        {
          var startTime = c.Start;

          Category category;
          var res = _categories.Where(cat => cat.Name == c.Class);
          if (res.Count() > 1)
          {
            category = res.Where(cat => cat.Name == c.Class && cat.Leg == c.Leg).First();
          }
          else
          {
            category = res.First();
          }



          double previousLegTime = 0;
          var status = c.Status;

          if (fullDataDto.Race.Type == "Staffetta" && category.Legs > 1 && c.Leg > 1)
          {
            var teamNumber = c.Bib.ToString().Substring(1);
            var teammates = fullDataDto.Competitors.Where(cc => cc.Class == c.Class && cc.Leg < c.Leg && cc.Bib.ToString().Substring(1) == teamNumber).ToList();

            foreach (var tm in teammates)
            {
              if (tm.Status != "CL")
                status = tm.Status;

              previousLegTime += tm.Time;
            }
          }


          Dictionary<int, int> intermediatesTimes = new();
          if (c.Status == "IP" || c.Status == "GA" || !c.Intermediates.Any())
          {
            intermediatesTimes = c.Radio.ToDictionary(i => i.Point, i => (int)Math.Floor(previousLegTime + i.Time));
          }
          else
          {
            foreach (var intermediate in category.Intermediates)
            {
              var value = c.Intermediates.Where(ii => ii.Point == intermediate.Code).Select(ii => (int)Math.Floor(previousLegTime + ii.Time)).FirstOrDefault();
              if (value != 0)
                intermediatesTimes.Add(intermediate.Code, value);
            }
          }

          var club = fullDataDto.Clubs.First(club => club.CountryId == c.ClubId);
          var item = new ResultItem()
          {
            Name = c.Name + " " + c.Surname + "<br>" + club.Country + " - " + club.Name,

            Country = c.Naz,
            StartDTO = firstStart + TimeSpan.FromSeconds(c.Start),
            Status = status,
            //Position = status == "CL" ? c.Pos : "",
            IntermediatesTimes = intermediatesTimes,
            TotalTime = (int)Math.Floor(previousLegTime + c.Time),
            SJ = c.SJ
          };

          item.Start = item.StartDTO.ToLocalTime().ToString("HH:mm:ss");

          _results[category.ShortName].Add(item);
        }

        foreach (var category in _categories)
        {

          foreach (var intermediate in category.Intermediates)
          {
            int i = 1;
            int lastValue = -1;
            int firstTime = -1;
            int lasti = 1;
            foreach (var item in _results[category.ShortName]
              .Where(c => c.IntermediatesTimes.ContainsKey(intermediate.Code) && c.IntermediatesTimes[intermediate.Code] != 0)
              .OrderBy(c => c.IntermediatesTimes[intermediate.Code]))
            {
              var value = item.IntermediatesTimes[intermediate.Code];

              if (item.Status == "CL" || item.Status == "IP" || item.Status == "GA")
              {
                if (firstTime < 0)
                  firstTime = value;

                if (value == lastValue)
                {
                  item.IntermediatesPositions[intermediate.Code] = lasti;
                }
                else
                {
                  item.IntermediatesPositions[intermediate.Code] = i;
                }

                i++;

                lastValue = value;
                lasti = item.IntermediatesPositions[intermediate.Code];

                var diff = value - firstTime;
                item.Intermediates[intermediate.Code] = TimeSpan.FromSeconds(value).ToHMS() + $" ({lasti})<br><small>+" + TimeSpan.FromSeconds(diff).ToHMS() + "</small>";
              }
              else
              {
                item.Intermediates[intermediate.Code] = TimeSpan.FromSeconds(value).ToHMS();
              }

            }
          }

          {
            // FINISH
            int i = 1;
            int lastValue = -1;
            int firstTime = -1;
            int lasti = 1;
            foreach (var item in _results[category.ShortName].Where(c => c.Status == "CL" && c.TotalTime != 0).OrderBy(c => c.TotalTime))
            {
              var value = item.TotalTime;

              if (firstTime < 0)
                firstTime = value;

              if (value == lastValue)
              {
                item.TotalPosition = lasti;
              }
              else
              {
                item.TotalPosition = i;
              }


              i++;

              lastValue = value;
              lasti = item.TotalPosition;

              var diff = value - firstTime;
              item.Total = TimeSpan.FromSeconds(value).ToHMS() + $" ({lasti})<br><small>+" + TimeSpan.FromSeconds(diff).ToHMS() + "</small>";

              if (item.SJ)
                item.Total = "*" + item.Total;

              item.Position = "" + item.TotalPosition;
            }
          }

          int baseOrder = 9000;
          foreach (var item in _results[category.ShortName].OrderBy(c => c.Status).ThenBy(c => c.Start).ThenBy(c => c.Name))
          {
            item.Order = baseOrder++;

            if (item.Status == "IP" || item.Status == "GA")
            {
              if (item.IntermediatesTimes.Any())
              {
                var lastIntermediateTime = item.IntermediatesTimes.Max(c => c.Value);
                var lastIntermediate = item.IntermediatesTimes.Where(c => c.Value == lastIntermediateTime).First().Key;

                var absTime = item.StartDTO + TimeSpan.FromSeconds(lastIntermediateTime);
                if (DateTimeOffset.UtcNow - absTime < lastUpdateInterval)
                {
                  item.LastUpdated = true;
                }

                item.Order = item.IntermediatesPositions[lastIntermediate] - 0.1f;
              }
              var diff = DateTimeOffset.UtcNow - item.StartDTO;
              item.Total = "<span data-running-time-from='" + item.StartDTO.ToUnixTimeSeconds() + "'>" + (diff < TimeSpan.Zero ? "" : diff.ToHMS()) + "</span>";
            }
            else if (item.Status == "CL")
            {
              item.Order = item.TotalPosition;


              var absTime = item.StartDTO + TimeSpan.FromSeconds(item.TotalTime);
              if (DateTimeOffset.UtcNow - absTime < lastUpdateInterval)
              {
                item.LastUpdated = true;
              }
            }
            else
            {
              item.Total = item.Status switch
              {
                "PM" => "MP",
                "PE" => "MP",
                "NP" => "DNS",
                "FT" => "OT",
                "RI" => "DNF",
                "SQ" => "DSQ",
                _ => item.Status,
              };


            }
          }

          _results[category.ShortName] = _results[category.ShortName].OrderBy(c => c.Order).ToList();

          _hubContext.Clients.Group(ResultsHub.GROUP_RESULTS + category.ShortName).UpdateResult(_results[category.ShortName]);
        }
      }
      catch (Exception e)
      {
        Log.Error(e, "error push");
      }

    }

  }
}

