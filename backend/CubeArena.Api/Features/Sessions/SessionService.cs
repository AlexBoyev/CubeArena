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
}
