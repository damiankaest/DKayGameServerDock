using System.Text.Json.Serialization;
using DKay.GameServerDock.Api;
using DKay.GameServerDock.Api.Endpoints;
using DKay.GameServerDock.Api.Hubs;
using DKay.GameServerDock.Application.Abstractions;
using DKay.GameServerDock.Infrastructure;
using DKay.GameServerDock.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

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
builder.Services.AddGameServerDockInfrastructure(builder.Configuration);
builder.Services.AddSingleton<IServerEventSink, SignalRServerEventSink>();
builder.Services.AddHostedService<ServerProvisioningWorker>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var databaseFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    await using var database = await databaseFactory.CreateDbContextAsync();
    await database.Database.EnsureCreatedAsync();
}

app.UseMiddleware<ApiExceptionMiddleware>();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();
app.MapAuthEndpoints();
app.MapServerEndpoints();
app.MapHub<ServerEventsHub>("/hubs/servers");
app.MapFallbackToFile("index.html").AllowAnonymous();

await app.RunAsync();

