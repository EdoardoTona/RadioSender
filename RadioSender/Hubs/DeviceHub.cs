using Microsoft.AspNetCore.SignalR;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Source.TmFRadio;
using RadioSender.Hubs.Devices;
using System.Threading.Tasks;

namespace RadioSender.Hubs
{
  public class DeviceHub : Hub
  {
    private readonly DeviceService _deviceService;
    public DeviceHub(DeviceService deviceService)
    {
      _deviceService = deviceService;
    }
    public async Task UpdateDeviceStatus(string device)
    {
      await Clients.All.SendAsync("DeviceStatus", device);
    }

    public override async Task OnConnectedAsync()
    {
      //await Groups.AddToGroupAsync(Context.ConnectionId, "SignalR Users");
      await base.OnConnectedAsync();

      _deviceService.Notify(Context.ConnectionId);
    }
  }
}