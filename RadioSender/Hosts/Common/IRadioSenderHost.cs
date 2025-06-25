using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Common;

public interface IRadioSenderHost
{
  public Task StartAsync(CancellationToken cancellationToken);
  public Task StopAsync(CancellationToken cancellationToken);
}
