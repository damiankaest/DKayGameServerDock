using System.Security.Claims;
using DKay.GameServerDock.Application.Abstractions;
using DKay.GameServerDock.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

namespace DKay.GameServerDock.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth");

        group.MapGet("/status", async (IUserRepository users, CancellationToken cancellationToken) =>
            Results.Ok(new { setupRequired = !await users.AnyAsync(cancellationToken) })).AllowAnonymous();

        group.MapPost("/bootstrap", BootstrapAsync).AllowAnonymous();
        group.MapPost("/login", LoginAsync).AllowAnonymous();
        group.MapPost("/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });
        group.MapGet("/me", (ClaimsPrincipal user) => Results.Ok(new { userName = user.Identity?.Name }));
        return endpoints;
    }

    private static async Task<IResult> BootstrapAsync(
        AuthRequest request,
        IUserRepository users,
        IClock clock,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (await users.AnyAsync(cancellationToken))
        {
            return Results.Conflict(new { error = "The local administrator has already been created." });
        }

        if (string.IsNullOrWhiteSpace(request.UserName) || request.Password.Length < 10)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["credentials"] = ["Use a user name and a password with at least 10 characters."]
            });
        }

        var passwordHasher = new PasswordHasher<LocalUser>();
        var placeholder = LocalUser.Create(request.UserName, "pending", clock.UtcNow);
        var user = LocalUser.Create(request.UserName, passwordHasher.HashPassword(placeholder, request.Password), clock.UtcNow);
        await users.AddAsync(user, cancellationToken);
        await SignInAsync(context, user);
        return Results.Ok(new { userName = user.UserName });
    }

    private static async Task<IResult> LoginAsync(
        AuthRequest request,
        IUserRepository users,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var user = await users.FindByNameAsync(request.UserName.Trim(), cancellationToken);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var result = new PasswordHasher<LocalUser>().VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
        {
            return Results.Unauthorized();
        }

        await SignInAsync(context, user);
        return Results.Ok(new { userName = user.UserName });
    }

    private static Task SignInAsync(HttpContext context, LocalUser user)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.UserName),
                new Claim(ClaimTypes.Role, "Administrator")
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);
        return context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(14) });
    }

    private sealed record AuthRequest(string UserName, string Password);
}
