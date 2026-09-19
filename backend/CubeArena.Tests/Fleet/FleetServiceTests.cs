using CubeArena.Api.Data;
using CubeArena.Api.Features.Fleet;
using CubeArena.Domain.Fleet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace CubeArena.Tests.Fleet;

public class FleetServiceTests
{
    private static readonly FleetOptions TestOptions = new()
    {
        ApiKey = "unit-test-fleet-key",
        HeartbeatInterval = TimeSpan.FromSeconds(10),
        MissedHeartbeatsBeforeStale = 3
    };

    private static (CubeArenaDbContext Db, FleetService Service, FakeTimeProvider Time) CreateService()
    {
        var db = new CubeArenaDbContext(new DbContextOptionsBuilder<CubeArenaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var service = new FleetService(db, Options.Create(TestOptions), time);
        return (db, service, time);
    }

    [Fact]
    public async Task RegisterAsync_CreatesAGameServerAndItsSession()
    {
        var (db, service, _) = CreateService();

        var (server, session) = await service.RegisterAsync("game-server-1", 7777, capacity: 4, CancellationToken.None);

        Assert.Equal(GameServerStatus.Active, server.Status);
        Assert.Equal(session.GameServerId, server.Id);
        Assert.Equal(1, await db.Sessions.CountAsync());
    }

    [Fact]
    public async Task HeartbeatAsync_UpdatesLastHeartbeat()
    {
        var (_, service, time) = CreateService();
        var (server, _) = await service.RegisterAsync("game-server-1", 7777, 4, CancellationToken.None);

        time.Advance(TimeSpan.FromSeconds(5));
        var outcome = await service.HeartbeatAsync(server.Id, playerCount: 2, CancellationToken.None);

        Assert.Equal(HeartbeatOutcome.Success, outcome);
    }

    [Fact]
    public async Task HeartbeatAsync_ReturnsNotFound_ForUnknownServer()
    {
        var (_, service, _) = CreateService();

        var outcome = await service.HeartbeatAsync(Guid.NewGuid(), playerCount: 0, CancellationToken.None);

        Assert.Equal(HeartbeatOutcome.NotFound, outcome);
    }

    [Fact]
    public async Task MarkStaleServersOfflineAsync_MarksServersOffline_AfterMissedHeartbeats()
    {
        var (db, service, time) = CreateService();
        var (server, _) = await service.RegisterAsync("game-server-1", 7777, 4, CancellationToken.None);

        // StaleThreshold = 10s * 3 = 30s.
        time.Advance(TimeSpan.FromSeconds(29));
        var tooEarly = await service.MarkStaleServersOfflineAsync(CancellationToken.None);
        Assert.Equal(0, tooEarly);

        time.Advance(TimeSpan.FromSeconds(2));
        var stale = await service.MarkStaleServersOfflineAsync(CancellationToken.None);
        Assert.Equal(1, stale);

        var reloaded = await db.GameServers.FindAsync(server.Id);
        Assert.Equal(GameServerStatus.Offline, reloaded!.Status);
    }

    [Fact]
    public async Task HeartbeatAsync_ReturnsOffline_ForAServerMarkedStale()
    {
        var (_, service, time) = CreateService();
        var (server, _) = await service.RegisterAsync("game-server-1", 7777, 4, CancellationToken.None);

        time.Advance(TimeSpan.FromSeconds(31));
        await service.MarkStaleServersOfflineAsync(CancellationToken.None);

        var outcome = await service.HeartbeatAsync(server.Id, playerCount: 0, CancellationToken.None);
        Assert.Equal(HeartbeatOutcome.Offline, outcome);
    }
}
