using System.IdentityModel.Tokens.Jwt;

namespace CubeArena.Api.Features.Sessions;

public record QuickplayResponse(string Host, int Port, Guid SessionId, string Ticket, int SlotIndex);
public record ErrorResponse(string Error);

public static class SessionEndpoints
{
    public static void MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/sessions/quickplay", async (HttpContext httpContext, SessionService sessions, CancellationToken ct) =>
        {
            var subClaim = httpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            if (subClaim is null || !Guid.TryParse(subClaim, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await sessions.QuickplayAsync(userId, ct);

            return result.Outcome switch
            {
                QuickplayOutcome.Success => Results.Ok(new QuickplayResponse(
                    result.Host!, result.Port!.Value, result.SessionId!.Value, result.Ticket!, result.SlotIndex!.Value)),
                _ => Results.Json(new ErrorResponse("no_capacity"), statusCode: StatusCodes.Status503ServiceUnavailable)
            };
        }).RequireAuthorization();

        app.MapGet("/.well-known/jwks.json", (TicketService tickets) => Results.Json(tickets.GetPublicJwks()));
    }
}
