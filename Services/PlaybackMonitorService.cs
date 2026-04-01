using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.KavasakiPresence.Services;

public class PlaybackMonitorService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // TODO: Implement playback monitoring
            await Task.Delay(1000, stoppingToken);
        }
    }
}
