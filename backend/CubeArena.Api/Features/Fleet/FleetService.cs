using CubeArena.Api.Data;
using CubeArena.Domain.Fleet;
using CubeArena.Domain.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CubeArena.Api.Features.Fleet;

public enum HeartbeatOutcome
{
    Success,
    NotFound,
    Offline
}

public class FleetService(
    CubeArenaDbContext db,
    IOptions<FleetOptions> options,
    TimeProvider timeProvider,
    ILogger<FleetService>? logger = null)
{
    private readonly FleetOptions _options = options.Value;

    public async Task<(GameServer Server, Session Session)> RegisterAsync(
        string host, int port, int capacity, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var server = new GameServer
        {
            Id = Guid.NewGuid(),
            Host = host,
            Port = port,
            Capacity = capacity,
            Status = GameServerStatus.Active,
            RegisteredAtUtc = now,
            LastHeartbeatAtUtc = now
        };

        var session = new Session
        {
            Id = Guid.NewGuid(),
            GameServerId = server.Id,
            CreatedAtUtc = now
        };

        db.GameServers.Add(server);
        db.Sessions.Add(session);
        await db.SaveChangesAsync(ct);

        logger?.LogInformation(
            "Game server registered. GameServerId={GameServerId} Host={Host} Port={Port} SessionId={SessionId}",
            server.Id, host, port, session.Id);

        return (server, session);
    }

    public async Task<HeartbeatOutcome> HeartbeatAsync(Guid gameServerId, int playerCount, CancellationToken ct)
    {
        var server = await db.GameServers.FindAsync([gameServerId], ct);
        if (server is null)
        {
            return HeartbeatOutcome.NotFound;
        }

        if (server.Status != GameServerStatus.Active)
        {
            return HeartbeatOutcome.Offline;
        }

        server.LastHeartbeatAtUtc = timeProvider.GetUtcNow();
        server.ReportedPlayerCount = playerCount;
        await db.SaveChangesAsync(ct);

        return HeartbeatOutcome.Success;
    }

    // Called by StaleFleetSweepService on a timer, and directly from tests.
    public async Task<int> MarkStaleServersOfflineAsync(CancellationToken ct)
    {
        var cutoff = timeProvider.GetUtcNow() - _options.StaleThreshold;
        var staleServers = await db.GameServers
            .Where(s => s.Status == GameServerStatus.Active && s.LastHeartbeatAtUtc < cutoff)
            .ToListAsync(ct);

        foreach (var server in staleServers)
        {
            server.Status = GameServerStatus.Offline;
            logger?.LogWarning(
                "Game server marked offline after missed heartbeats. GameServerId={GameServerId} LastHeartbeatAtUtc={LastHeartbeatAtUtc}",
                server.Id, server.LastHeartbeatAtUtc);
        }

        if (staleServers.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return staleServers.Count;
    }
}
