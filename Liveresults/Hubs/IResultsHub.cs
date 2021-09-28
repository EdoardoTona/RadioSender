using Common;
using Liveresults.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Liveresults.Hubs
{
  public interface IResultsHub
  {
    Task Abort();
    Task UpdateResult(IEnumerable<ResultItem> results);
    Task UpdateCategories(IEnumerable<Category> categories);
  }
}
