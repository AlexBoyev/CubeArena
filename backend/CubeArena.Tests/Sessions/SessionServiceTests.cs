using CubeArena.Api.Data;
using CubeArena.Api.Features.Fleet;
using CubeArena.Api.Features.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace CubeArena.Tests.Sessions;

public class SessionServiceTests
{
    private const string TestSigningKeyPem = """
        -----BEGIN EC PRIVATE KEY-----
        MHcCAQEEIPL41AQaBDyEWp74wh4x7HHJTLk/H+wGMuX8vhClj5BboAoGCCqGSM49
        AwEHoUQDQgAEu1onIul7buAJHIFBughJQsNefqRgfZtEkRifDqF9/ogN8HMejaZK
        XtRU0QIkBnTqI04BXCQKH4y+H2jUTvVfLw==
        -----END EC PRIVATE KEY-----
        """;

    private static readonly TicketOptions TicketTestOptions = new()
    {
        SigningKeyPem = TestSigningKeyPem,
        TicketLifetime = TimeSpan.FromSeconds(60)
    };

    private static (CubeArenaDbContext Db, SessionService Sessions, FleetService Fleet, FakeTimeProvider Time) CreateServices()
    {
        var db = new CubeArenaDbContext(new DbContextOptionsBuilder<CubeArenaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var tickets = new TicketService(Options.Create(TicketTestOptions), time);
        var sessions = new SessionService(db, tickets, Options.Create(TicketTestOptions), time);
        var fleet = new FleetService(db, Options.Create(new FleetOptions { ApiKey = "k" }), time);
        return (db, sessions, fleet, time);
    }

    [Fact]
    public async Task QuickplayAsync_ReturnsNoCapacity_WhenNoServerIsRegistered()
    {
        var (_, sessions, _, _) = CreateServices();

        var result = await sessions.QuickplayAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(QuickplayOutcome.NoCapacity, result.Outcome);
    }

    [Fact]
    public async Task QuickplayAsync_AllocatesASlot_OnARegisteredServer()
    {
        var (_, sessions, fleet, _) = CreateServices();
        var (server, session) = await fleet.RegisterAsync("game-server-1", 7777, 4, CancellationToken.None);

        var result = await sessions.QuickplayAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(QuickplayOutcome.Success, result.Outcome);
        Assert.Equal(server.Host, result.Host);
        Assert.Equal(server.Port, result.Port);
        Assert.Equal(session.Id, result.SessionId);
        Assert.Equal(0, result.SlotIndex);
        Assert.False(string.IsNullOrEmpty(result.Ticket));
    }

    [Fact]
    public async Task QuickplayAsync_AssignsDistinctSlots_ToDifferentUsers()
    {
        var (_, sessions, fleet, _) = CreateServices();
        await fleet.RegisterAsync("game-server-1", 7777, 4, CancellationToken.None);

        var first = await sessions.QuickplayAsync(Guid.NewGuid(), CancellationToken.None);
        var second = await sessions.QuickplayAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.NotEqual(first.SlotIndex, second.SlotIndex);
    }

    [Fact]
    public async Task QuickplayAsync_IsIdempotent_ForTheSameUserWithAnActiveReservation()
    {
        var (_, sessions, fleet, _) = CreateServices();
        await fleet.RegisterAsync("game-server-1", 7777, 4, CancellationToken.None);
        var userId = Guid.NewGuid();

        var first = await sessions.QuickplayAsync(userId, CancellationToken.None);
        var second = await sessions.QuickplayAsync(userId, CancellationToken.None);

        Assert.Equal(first.SessionId, second.SessionId);
        Assert.Equal(first.SlotIndex, second.SlotIndex);
        Assert.NotEqual(first.Ticket, second.Ticket); // fresh ticket each time
    }

    [Fact]
    public async Task QuickplayAsync_RejectsTheFifthPlayer_OnA4CapacityServer()
    {
        var (_, sessions, fleet, _) = CreateServices();
        await fleet.RegisterAsync("game-server-1", 7777, capacity: 4, CancellationToken.None);

        for (var i = 0; i < 4; i++)
        {
            var result = await sessions.QuickplayAsync(Guid.NewGuid(), CancellationToken.None);
            Assert.Equal(QuickplayOutcome.Success, result.Outcome);
        }

        var fifth = await sessions.QuickplayAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(QuickplayOutcome.NoCapacity, fifth.Outcome);
    }

    [Fact]
    public async Task QuickplayAsync_RejectsTheSeventhPlayer_OnA6CapacityServer()
    {
        var (_, sessions, fleet, _) = CreateServices();
        await fleet.RegisterAsync("game-server-1", 7777, capacity: 6, CancellationToken.None);

        for (var i = 0; i < 6; i++)
        {
            var result = await sessions.QuickplayAsync(Guid.NewGuid(), CancellationToken.None);
            Assert.Equal(QuickplayOutcome.Success, result.Outcome);
        }

        var seventh = await sessions.QuickplayAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(QuickplayOutcome.NoCapacity, seventh.Outcome);
    }

    [Fact]
    public async Task QuickplayAsync_ReusesAnExpiredSlot_AfterItsReservationLapses()
    {
        var (_, sessions, fleet, time) = CreateServices();
        await fleet.RegisterAsync("game-server-1", 7777, capacity: 1, CancellationToken.None);

        var first = await sessions.QuickplayAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.Equal(QuickplayOutcome.Success, first.Outcome);

        time.Advance(TimeSpan.FromSeconds(61));

        var second = await sessions.QuickplayAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(QuickplayOutcome.Success, second.Outcome);
        Assert.Equal(0, second.SlotIndex);
    }

    [Fact]
    public async Task QuickplayAsync_SkipsAFullServer_AndUsesAnotherWithRoom()
    {
        var (_, sessions, fleet, _) = CreateServices();
        await fleet.RegisterAsync("full-server", 7777, capacity: 1, CancellationToken.None);
        var (_, roomySession) = await fleet.RegisterAsync("roomy-server", 7778, capacity: 4, CancellationToken.None);

        await sessions.QuickplayAsync(Guid.NewGuid(), CancellationToken.None); // fills full-server's only slot

        var result = await sessions.QuickplayAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(QuickplayOutcome.Success, result.Outcome);
        Assert.Equal(roomySession.Id, result.SessionId);
    }

    [Fact]
    public async Task ConfirmSlotAsync_ExtendsExpiryPastTheTicketLifetime()
    {
        var (db, sessions, fleet, time) = CreateServices();
        await fleet.RegisterAsync("game-server-1", 7777, capacity: 4, CancellationToken.None);
        var userId = Guid.NewGuid();
        var quickplay = await sessions.QuickplayAsync(userId, CancellationToken.None);

        var confirmed = await sessions.ConfirmSlotAsync(quickplay.SessionId!.Value, userId, CancellationToken.None);
        Assert.True(confirmed);

        // Past the 60s ticket lifetime, the slot must still be held because it was confirmed.
        time.Advance(TimeSpan.FromSeconds(90));
        var stillActive = await db.SessionSlots.AnyAsync(s => s.UserId == userId && s.ExpiresAtUtc > time.GetUtcNow());
        Assert.True(stillActive);
    }

    [Fact]
    public async Task ConfirmSlotAsync_ReturnsFalse_ForAnUnknownUser()
    {
        var (_, sessions, fleet, _) = CreateServices();
        await fleet.RegisterAsync("game-server-1", 7777, capacity: 4, CancellationToken.None);

        var confirmed = await sessions.ConfirmSlotAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.False(confirmed);
    }

    [Fact]
    public async Task ReleaseThenRejoin_ReturnsTheSameSessionAndSlot()
    {
        var (_, sessions, fleet, time) = CreateServices();
        await fleet.RegisterAsync("game-server-1", 7777, capacity: 4, CancellationToken.None);
        var userId = Guid.NewGuid();

        var original = await sessions.QuickplayAsync(userId, CancellationToken.None);
        await sessions.ConfirmSlotAsync(original.SessionId!.Value, userId, CancellationToken.None);

        // Disconnect: starts the rejoin grace period.
        var released = await sessions.ReleaseSlotAsync(original.SessionId.Value, userId, CancellationToken.None);
        Assert.True(released);

        // Well within the grace period, quickplay again returns the same slot.
        time.Advance(TimeSpan.FromSeconds(30));
        var rejoined = await sessions.QuickplayAsync(userId, CancellationToken.None);

        Assert.Equal(QuickplayOutcome.Success, rejoined.Outcome);
        Assert.Equal(original.SessionId, rejoined.SessionId);
        Assert.Equal(original.SlotIndex, rejoined.SlotIndex);
    }

    [Fact]
    public async Task ReleaseSlot_AfterGracePeriodExpires_FreesTheSlotForSomeoneElse()
    {
        var (_, sessions, fleet, time) = CreateServices();
        await fleet.RegisterAsync("game-server-1", 7777, capacity: 1, CancellationToken.None);
        var userId = Guid.NewGuid();

        var original = await sessions.QuickplayAsync(userId, CancellationToken.None);
        await sessions.ConfirmSlotAsync(original.SessionId!.Value, userId, CancellationToken.None);
        await sessions.ReleaseSlotAsync(original.SessionId.Value, userId, CancellationToken.None);

        // Past the rejoin grace period, the slot is free for a new player.
        time.Advance(SessionService.RejoinGracePeriod + TimeSpan.FromSeconds(1));
        var newPlayer = await sessions.QuickplayAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(QuickplayOutcome.Success, newPlayer.Outcome);
        Assert.Equal(original.SessionId, newPlayer.SessionId);
        Assert.Equal(original.SlotIndex, newPlayer.SlotIndex);
    }
}
