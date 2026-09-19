using Microsoft.Extensions.Options;

namespace CubeArena.Api.Features.Fleet;

public class StaleFleetSweepService(IServiceScopeFactory scopeFactory, IOptions<FleetOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.HeartbeatInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            using var scope = scopeFactory.CreateScope();
            var fleet = scope.ServiceProvider.GetRequiredService<FleetService>();
            await fleet.MarkStaleServersOfflineAsync(stoppingToken);
        }
    }
}
