using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace CubeArena.Api.Features.Auth;

public record RegisterRequest(string Email, string Password);
public record LoginRequest(string Email, string Password);
public record RefreshRequest(string RefreshToken);
public record LogoutRequest(string RefreshToken);
public record TokenResponse(string AccessToken, string RefreshToken, int ExpiresInSeconds);
public record ErrorResponse(string Error);

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").RequireRateLimiting("auth");

        group.MapPost("/register", async (HttpContext httpContext, RegisterRequest request, AuthService auth, CancellationToken ct) =>
        {
            if (!IsValidEmail(request.Email))
            {
                return Results.BadRequest(new ErrorResponse("invalid_email"));
            }

            if (request.Password.Length < 8)
            {
                return Results.BadRequest(new ErrorResponse("password_too_short"));
            }

            var outcome = await auth.RegisterAsync(request.Email, request.Password, ct, httpContext.TraceIdentifier);

            return outcome switch
            {
                RegisterOutcome.Success => Results.Created("/auth/register", new { }),
                RegisterOutcome.EmailTaken => Results.Conflict(new ErrorResponse("email_taken")),
                _ => Results.Problem()
            };
        });

        group.MapPost("/login", async (HttpContext httpContext, LoginRequest request, AuthService auth, IOptions<AuthOptions> options, CancellationToken ct) =>
        {
            var result = await auth.LoginAsync(request.Email, request.Password, ct, httpContext.TraceIdentifier);

            switch (result.Outcome)
            {
                case LoginOutcome.Success:
                    return Results.Ok(new TokenResponse(
                        result.AccessToken!,
                        result.RefreshToken!,
                        (int)options.Value.AccessTokenLifetime.TotalSeconds));

                case LoginOutcome.LockedOut:
                    httpContext.Response.Headers.RetryAfter = ((int)result.RetryAfter!.Value.TotalSeconds).ToString();
                    return Results.Json(new ErrorResponse("locked_out"), statusCode: StatusCodes.Status429TooManyRequests);

                default:
                    return Results.Json(new ErrorResponse("invalid_credentials"), statusCode: StatusCodes.Status401Unauthorized);
            }
        });

        group.MapPost("/refresh", async (HttpContext httpContext, RefreshRequest request, TokenService tokens, IOptions<AuthOptions> options, CancellationToken ct) =>
        {
            var result = await tokens.RotateRefreshTokenAsync(request.RefreshToken, ct, httpContext.TraceIdentifier);

            if (result.Outcome != RefreshOutcome.Success)
            {
                var reason = result.Outcome switch
                {
                    RefreshOutcome.NotFound => "not_found",
                    RefreshOutcome.Expired => "expired",
                    RefreshOutcome.Reused => "reused",
                    RefreshOutcome.Revoked => "revoked",
                    _ => "invalid"
                };
                return Results.Json(new ErrorResponse(reason), statusCode: StatusCodes.Status401Unauthorized);
            }

            return Results.Ok(new TokenResponse(
                result.AccessToken!,
                result.RefreshToken!,
                (int)options.Value.AccessTokenLifetime.TotalSeconds));
        });

        group.MapPost("/logout", async (LogoutRequest request, TokenService tokens, CancellationToken ct) =>
        {
            await tokens.RevokeByRawTokenAsync(request.RefreshToken, ct);
            return Results.NoContent();
        });

        return group;
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            _ = new MailAddress(email);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
