using System.Text.Json.Serialization;
using DKay.GameServerDock.Api;
using DKay.GameServerDock.Api.Endpoints;
using DKay.GameServerDock.Api.Hubs;
using DKay.GameServerDock.Application.Abstractions;
using DKay.GameServerDock.Infrastructure;
using DKay.GameServerDock.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(options => options.ServiceName = "DKay Game Server Dock");

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSignalR().AddJsonProtocol(options =>
    options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "dkay.dock.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("public-guest", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});
builder.Services.AddGameServerDockInfrastructure(builder.Configuration);
builder.Services.AddSingleton<IServerEventSink, SignalRServerEventSink>();
builder.Services.AddHostedService<ServerProvisioningWorker>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var databaseFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    await using var database = await databaseFactory.CreateDbContextAsync();
    await database.Database.EnsureCreatedAsync();
    // EnsureCreated does not add newly introduced indexes to an existing SQLite database.
    await database.Database.ExecuteSqlRawAsync(
        """CREATE INDEX IF NOT EXISTS "IX_ServerEvents_ServerId_Id" ON "ServerEvents" ("ServerId", "Id" DESC);""");
}

app.UseMiddleware<ApiExceptionMiddleware>();
var dockOptions = app.Services.GetRequiredService<DockOptions>();
app.Use(async (context, next) =>
{
    if (context.Connection.LocalPort != dockOptions.PublicPortalPort)
    {
        await next();
        return;
    }

    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
    if (context.Request.Path.StartsWithSegments("/api/public"))
    {
        context.Response.Headers["Cache-Control"] = "no-store";
    }

    if (!dockOptions.PublicPortalEnabled)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    if (context.Request.Path == "/")
    {
        context.Response.Redirect("/join");
        return;
    }

    if (!IsPublicPortalPath(context.Request.Path))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();
app.MapAuthEndpoints();
app.MapServerEndpoints();
app.MapHub<ServerEventsHub>("/hubs/servers");
app.MapFallbackToFile("index.html").AllowAnonymous();

await app.RunAsync();

static bool IsPublicPortalPath(PathString path)
{
    if (path == "/join" ||
        path == "/api/public/servers" ||
        path == "/health" ||
        path == "/index.html")
    {
        return true;
    }

    var extension = Path.GetExtension(path.Value);
    return extension is ".js" or ".css" or ".ico" or ".svg" or ".png" or ".webp" or ".woff" or ".woff2";
}
