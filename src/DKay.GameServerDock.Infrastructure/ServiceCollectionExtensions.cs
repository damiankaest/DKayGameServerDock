using DKay.GameServerDock.Application.Abstractions;
using DKay.GameServerDock.Application.Services;
using DKay.GameServerDock.Infrastructure.Games;
using DKay.GameServerDock.Infrastructure.Installation;
using DKay.GameServerDock.Infrastructure.Monitoring;
using DKay.GameServerDock.Infrastructure.Persistence;
using DKay.GameServerDock.Infrastructure.Processes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DKay.GameServerDock.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGameServerDockInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = ResolveOptions(configuration);
        Directory.CreateDirectory(options.DataRoot);
        Directory.CreateDirectory(options.ServersRoot);

        services.AddSingleton(options);
        services.AddDbContextFactory<AppDbContext>(builder =>
            builder.UseSqlite($"Data Source={Path.Combine(options.DataRoot, "dkay-game-server-dock.db")}"));

        services.AddScoped<IServerRepository, ServerRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ServerOrchestrator>();

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IHostMetricsProvider, HostMetricsProvider>();
        services.AddSingleton<IPathPolicy>(_ => new PathPolicy(options.ServersRoot));
        services.AddSingleton<IServerWorkQueue, ServerWorkQueue>();
        services.AddSingleton<IServerRuntimeStateStore, ServerRuntimeStateStore>();
        services.AddSingleton<IProcessSupervisor, ManagedProcessSupervisor>();

        services.AddSingleton(new HttpClient());
        services.AddSingleton<PaperInstaller>();
        services.AddSingleton<Cs2Installer>();
        services.AddSingleton<IGameModule, PaperGameModule>();
        services.AddSingleton<IGameModule, Cs2GameModule>();
        services.AddSingleton<IGameModuleRegistry, GameModuleRegistry>();

        return services;
    }

    private static DockOptions ResolveOptions(IConfiguration configuration)
    {
        var options = configuration.GetSection(DockOptions.SectionName).Get<DockOptions>() ?? new DockOptions();
        var defaultDataRoot = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DKayGameServerDock")
            : Path.Combine(AppContext.BaseDirectory, "data");
        var defaultServersRoot = OperatingSystem.IsWindows()
            ? @"C:\GameServers"
            : Path.Combine(AppContext.BaseDirectory, "servers");

        options.DataRoot = ResolvePath("DGS_DATA_ROOT", options.DataRoot, defaultDataRoot);
        options.ServersRoot = ResolvePath("DGS_SERVERS_ROOT", options.ServersRoot, defaultServersRoot);
        options.SteamCmdPath = FirstConfigured("DGS_STEAMCMD_PATH", options.SteamCmdPath, string.Empty);
        options.JavaPath = FirstConfigured("DGS_JAVA_PATH", options.JavaPath, "java");
        return options;
    }

    private static string ResolvePath(string environmentName, string configured, string fallback) =>
        Path.GetFullPath(FirstConfigured(environmentName, configured, fallback));

    private static string FirstConfigured(string environmentName, string configured, string fallback)
    {
        var environmentValue = Environment.GetEnvironmentVariable(environmentName);
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return environmentValue;
        }

        return string.IsNullOrWhiteSpace(configured) ? fallback : configured;
    }
}
