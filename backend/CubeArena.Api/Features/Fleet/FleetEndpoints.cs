using System.Security.Cryptography;
using System.Text;
using CubeArena.Api.Features.Sessions;
using Microsoft.Extensions.Options;

namespace CubeArena.Api.Features.Fleet;

public record RegisterServerRequest(string Host, int Port, int Capacity = 4);
public record RegisterServerResponse(Guid GameServerId, Guid SessionId);
public record HeartbeatRequest(Guid GameServerId, int PlayerCount);
public record SlotSessionRequest(Guid SessionId, Guid UserId);

public static class FleetEndpoints
{
    public static RouteGroupBuilder MapFleetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/fleet").AddEndpointFilter(RequireFleetApiKey);

        group.MapPost("/register", async (RegisterServerRequest request, FleetService fleet, CancellationToken ct) =>
        {
            var (server, session) = await fleet.RegisterAsync(request.Host, request.Port, request.Capacity, ct);
            return Results.Ok(new RegisterServerResponse(server.Id, session.Id));
        });

        group.MapPost("/heartbeat", async (HeartbeatRequest request, FleetService fleet, CancellationToken ct) =>
        {
            var outcome = await fleet.HeartbeatAsync(request.GameServerId, request.PlayerCount, ct);
            return outcome switch
            {
                HeartbeatOutcome.Success => Results.NoContent(),
                HeartbeatOutcome.Offline => Results.Conflict(new { error = "server_offline" }),
                _ => Results.NotFound(new { error = "server_not_found" })
            };
        });

        group.MapPost("/sessions/confirm", async (SlotSessionRequest request, SessionService sessions, CancellationToken ct) =>
        {
            var found = await sessions.ConfirmSlotAsync(request.SessionId, request.UserId, ct);
            return found ? Results.NoContent() : Results.NotFound(new { error = "slot_not_found" });
        });

        group.MapPost("/sessions/release", async (SlotSessionRequest request, SessionService sessions, CancellationToken ct) =>
        {
            var found = await sessions.ReleaseSlotAsync(request.SessionId, request.UserId, ct);
            return found ? Results.NoContent() : Results.NotFound(new { error = "slot_not_found" });
        });

        return group;
    }

    private static async ValueTask<object?> RequireFleetApiKey(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var options = context.HttpContext.RequestServices.GetRequiredService<IOptions<FleetOptions>>().Value;
        var provided = context.HttpContext.Request.Headers["X-Fleet-Api-Key"].ToString();

        var expectedBytes = Encoding.UTF8.GetBytes(options.ApiKey);
        var providedBytes = Encoding.UTF8.GetBytes(provided);

        if (providedBytes.Length != expectedBytes.Length || !CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes))
        {
            return Results.Unauthorized();
        }

        return await next(context);
    }
}
