using CubeArena.Api.Data;
using CubeArena.Domain.Fleet;
using CubeArena.Domain.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CubeArena.Api.Features.Sessions;

public enum QuickplayOutcome
{
    Success,
    NoCapacity
}

public record QuickplayResult(
    QuickplayOutcome Outcome,
    string? Host = null,
    int? Port = null,
    Guid? SessionId = null,
    string? Ticket = null,
    int? SlotIndex = null);

public class SessionService(
    CubeArenaDbContext db,
    TicketService tickets,
    IOptions<TicketOptions> ticketOptions,
    TimeProvider timeProvider)
{
    // How long a disconnected player's slot stays reserved before it's free for
    // someone else to take (section 6: "rejoin into the same session if a slot
    // is still reserved"). Deliberately longer than the 60s connect-ticket
    // lifetime, which only governs how long a fresh ticket is valid for.
    public static readonly TimeSpan RejoinGracePeriod = TimeSpan.FromMinutes(2);

    // A slot is "confirmed" (the player is actually connected to the game server)
    // by extending its expiry far past any realistic match length, so quickplay's
    // occupancy check keeps treating it as occupied without needing a schema change.
    private static readonly TimeSpan ConfirmedSlotLifetime = TimeSpan.FromHours(24);


    public async Task<QuickplayResult> QuickplayAsync(Guid userId, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var ticketLifetime = ticketOptions.Value.TicketLifetime;

        // Rejoin-safe: if this user already holds an unexpired reservation, refresh it
        // and hand out a new ticket for the same session/slot instead of double-booking.
        var existingSlot = await db.SessionSlots
            .Where(s => s.UserId == userId && s.ExpiresAtUtc > now)
            .OrderByDescending(s => s.ReservedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (existingSlot is not null)
        {
            var existingSession = await db.Sessions.FirstAsync(s => s.Id == existingSlot.SessionId, ct);
            var existingServer = await db.GameServers.FirstAsync(g => g.Id == existingSession.GameServerId, ct);

            existingSlot.ExpiresAtUtc = now + ticketLifetime;
            await db.SaveChangesAsync(ct);

            var refreshedTicket = tickets.CreateTicket(existingSession.Id, userId, existingSlot.SlotIndex);
            return new QuickplayResult(
                QuickplayOutcome.Success, existingServer.Host, existingServer.Port,
                existingSession.Id, refreshedTicket, existingSlot.SlotIndex);
        }

        var candidates = await (
            from server in db.GameServers
            where server.Status == GameServerStatus.Active
            join session in db.Sessions on server.Id equals session.GameServerId
            select new { server, session }
        ).ToListAsync(ct);

        foreach (var candidate in candidates)
        {
            var occupiedSlots = await db.SessionSlots
                .Where(s => s.SessionId == candidate.session.Id && s.ExpiresAtUtc > now)
                .Select(s => s.SlotIndex)
                .ToListAsync(ct);

            if (occupiedSlots.Count >= candidate.server.Capacity)
            {
                continue;
            }

            var slotIndex = Enumerable.Range(0, candidate.server.Capacity).Except(occupiedSlots).First();

            db.SessionSlots.Add(new SessionSlot
            {
                Id = Guid.NewGuid(),
                SessionId = candidate.session.Id,
                UserId = userId,
                SlotIndex = slotIndex,
                ReservedAtUtc = now,
                ExpiresAtUtc = now + ticketLifetime
            });
            await db.SaveChangesAsync(ct);

            var ticket = tickets.CreateTicket(candidate.session.Id, userId, slotIndex);
            return new QuickplayResult(
                QuickplayOutcome.Success, candidate.server.Host, candidate.server.Port,
                candidate.session.Id, ticket, slotIndex);
        }

        return new QuickplayResult(QuickplayOutcome.NoCapacity);
    }

    // Called by the game server when a client's connect ticket is approved, so the
    // reservation outlives the ticket's own 60s lifetime for as long as they're connected.
    public async Task<bool> ConfirmSlotAsync(Guid sessionId, Guid userId, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var slot = await db.SessionSlots
            .Where(s => s.SessionId == sessionId && s.UserId == userId && s.ExpiresAtUtc > now)
            .OrderByDescending(s => s.ReservedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (slot is null)
        {
            return false;
        }

        slot.ExpiresAtUtc = now + ConfirmedSlotLifetime;
        await db.SaveChangesAsync(ct);
        return true;
    }

    // Called by the game server when a client disconnects, starting the rejoin grace
    // period instead of leaving the slot reserved indefinitely (ConfirmedSlotLifetime).
    public async Task<bool> ReleaseSlotAsync(Guid sessionId, Guid userId, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var slot = await db.SessionSlots
            .Where(s => s.SessionId == sessionId && s.UserId == userId && s.ExpiresAtUtc > now)
            .OrderByDescending(s => s.ReservedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (slot is null)
        {
            return false;
        }

        slot.ExpiresAtUtc = now + RejoinGracePeriod;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
