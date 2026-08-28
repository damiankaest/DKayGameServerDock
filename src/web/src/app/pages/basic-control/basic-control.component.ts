import { Component, DestroyRef, inject, signal } from '@angular/core';
import { catchError, finalize, forkJoin, of } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { ConsoleCommandResult, Cs2BasicConfiguration, Cs2LoadedPlugin, Cs2LocalMap, Cs2MapChangeState, Cs2ModeCatalog, Cs2PluginState, Cs2WorkshopMap, GameServer } from '../../core/models';

interface CommandEntry {
  command: string;
  output: string;
  transport: string;
  failed: boolean;
  createdAt: Date;
}

@Component({
  selector: 'app-basic-control',
  templateUrl: './basic-control.component.html',
  styleUrl: './basic-control.component.scss',
})
export class BasicControlComponent {
  private readonly api = inject(ApiService);
  private readonly destroyRef = inject(DestroyRef);
  private refreshHandle: ReturnType<typeof setInterval> | null = null;

  readonly servers = signal<GameServer[]>([]);
  readonly selectedServerId = signal('');
  readonly selectedServer = signal<GameServer | null>(null);
  readonly loading = signal(true);
  readonly importName = signal('Mein CS2 Server');
  readonly importDirectory = signal('');
  readonly importPort = signal(27015);
  readonly importRamLimitMb = signal(4096);
  readonly importing = signal(false);
  readonly importMessage = signal('');
  readonly actioning = signal('');
  readonly command = signal('');
  readonly commandRunning = signal(false);
  readonly commandHistory = signal<CommandEntry[]>([]);
  readonly basicConfiguration = signal<Cs2BasicConfiguration | null>(null);
  readonly configSaving = signal(false);
  readonly configMessage = signal('');
  readonly modeCatalog = signal<Cs2ModeCatalog | null>(null);
  readonly mapQuery = signal('');
  readonly localMapResults = signal<Cs2LocalMap[]>([]);
  readonly workshopResults = signal<Cs2WorkshopMap[]>([]);
  readonly mapSearching = signal(false);
  readonly mapQueueing = signal('');
  readonly mapChangeDelay = signal(30);
  readonly mapChangeState = signal<Cs2MapChangeState | null>(null);
  readonly mapMessage = signal('');
  readonly pluginState = signal<Cs2PluginState | null>(null);
  readonly pluginActioning = signal('');
  readonly error = signal('');

  constructor() {
    this.loadModeCatalog();
    this.loadServers();
    this.refreshHandle = setInterval(() => {
      this.refreshSelectedServer(true);
      this.loadMapChangeState();
      this.loadPluginState();
    }, 3000);
    this.destroyRef.onDestroy(() => {
      if (this.refreshHandle !== null) clearInterval(this.refreshHandle);
    });
  }

  selectServer(event: Event): void {
    const serverId = (event.target as HTMLSelectElement).value;
    this.selectedServerId.set(serverId);
    this.commandHistory.set([]);
    this.basicConfiguration.set(null);
    this.configMessage.set('');
    this.mapQuery.set('');
    this.localMapResults.set([]);
    this.workshopResults.set([]);
    this.mapMessage.set('');
    this.mapChangeState.set(null);
    this.pluginState.set(null);
    this.error.set('');
    this.refreshSelectedServer();
    this.loadBasicConfiguration(serverId);
    this.loadMapChangeState();
    this.loadPluginState();
  }

  updateImportText(key: 'name' | 'directory', event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    if (key === 'name') this.importName.set(value);
    else this.importDirectory.set(value);
  }

  updateImportNumber(key: 'port' | 'ramLimitMb', event: Event): void {
    const value = Number((event.target as HTMLInputElement).value);
    if (key === 'port') this.importPort.set(value);
    else this.importRamLimitMb.set(value);
  }

  importExistingCs2(): void {
    const name = this.importName().trim();
    const installDirectory = this.importDirectory().trim();
    if (!name || !installDirectory || this.importing()) return;

    this.error.set('');
    this.importMessage.set('');
    this.importing.set(true);
    this.api
      .importExistingCs2Server({
        name,
        installDirectory,
        port: this.importPort(),
        ramLimitMb: this.importRamLimitMb(),
      })
      .pipe(finalize(() => this.importing.set(false)))
      .subscribe({
        next: (server) => {
          this.servers.update((servers) =>
            [...servers, server].sort((left, right) => left.name.localeCompare(right.name)),
          );
          this.selectedServerId.set(server.id);
          this.selectedServer.set(server);
          this.commandHistory.set([]);
          this.importMessage.set(
            'Der vorhandene Ordner wurde registriert. Es wurden keine Spieldateien installiert oder verschoben.',
          );
          this.loadBasicConfiguration(server.id);
        },
        error: (error) =>
          this.error.set(error.error?.detail ?? 'Der CS2-Ordner konnte nicht registriert werden.'),
      });
  }

  action(action: 'start' | 'stop' | 'restart'): void {
    const server = this.selectedServer();
    if (!server) return;

    this.error.set('');
    this.actioning.set(action);
    this.api
      .serverAction(server.id, action)
      .pipe(finalize(() => this.actioning.set('')))
      .subscribe({
        next: () => this.refreshSelectedServer(),
        error: (error) =>
          this.error.set(
            error.error?.detail ?? `Server konnte nicht ${this.actionLabel(action)} werden.`,
          ),
      });
  }

  updateCommand(event: Event): void {
    this.command.set((event.target as HTMLInputElement).value);
  }

  useCommand(command: string): void {
    this.command.set(command);
  }

  sendCommand(): void {
    const server = this.selectedServer();
    const command = this.command().trim();
    if (!server || !command || this.commandRunning()) return;

    this.error.set('');
    this.commandRunning.set(true);
    this.api
      .sendCommand(server.id, command)
      .pipe(finalize(() => this.commandRunning.set(false)))
      .subscribe({
        next: (result) => {
          this.addCommandEntry(command, result);
          this.command.set('');
        },
        error: (error) => {
          const message = error.error?.detail ?? 'Command konnte nicht ausgeführt werden.';
          this.commandHistory.update((history) =>
            [
              {
                command,
                output: message,
                transport: 'error',
                failed: true,
                createdAt: new Date(),
              },
              ...history,
            ].slice(0, 50),
          );
          this.error.set(message);
        },
      });
  }

  clearHistory(): void {
    this.commandHistory.set([]);
  }

  updateAutoBhop(event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    this.basicConfiguration.update((configuration) =>
      configuration ? { ...configuration, autoBhop: checked } : null,
    );
  }

  updateBasicNumber(key: 'gravity' | 'botQuota', event: Event): void {
    const value = Number((event.target as HTMLInputElement).value);
    this.basicConfiguration.update((configuration) =>
      configuration ? { ...configuration, [key]: value } : null,
    );
  }

  saveBasicConfiguration(): void {
    const server = this.selectedServer();
    const configuration = this.basicConfiguration();
    if (!server || !configuration || this.configSaving()) return;

    this.error.set('');
    this.configMessage.set('');
    this.configSaving.set(true);
    this.api
      .saveCs2BasicConfiguration(server.id, configuration)
      .pipe(finalize(() => this.configSaving.set(false)))
      .subscribe({
        next: (result) => {
          this.basicConfiguration.set(result.configuration);
          this.configMessage.set(result.message);
          if (result.output) {
            this.commandHistory.update((history) =>
              [
                {
                  command: 'exec dkay-basic.cfg',
                  output: result.output!,
                  transport: result.appliedLive ? 'rcon · verified' : 'rcon · verification failed',
                  failed: result.running && !result.appliedLive,
                  createdAt: new Date(),
                },
                ...history,
              ].slice(0, 50),
            );
          }
        },
        error: (error) =>
          this.error.set(
            error.error?.detail ?? 'Basic-Konfiguration konnte nicht gespeichert werden.',
          ),
      });
  }

  updateMapQuery(event: Event): void {
    this.mapQuery.set((event.target as HTMLInputElement).value);
  }

  updateMapChangeDelay(event: Event): void {
    this.mapChangeDelay.set(Number((event.target as HTMLSelectElement).value));
  }

  searchMaps(): void {
    const server = this.selectedServer();
    const query = this.mapQuery().trim();
    if (!server || server.templateId !== 'counter-strike-2' || query.length < 2) return;

    this.error.set('');
    this.mapMessage.set('');
    this.mapSearching.set(true);
    forkJoin({
      local: this.api.searchCs2LocalMaps(server.id, query),
      workshop: this.api.searchCs2Workshop(server.id, query).pipe(catchError(() => of(null))),
    })
      .pipe(finalize(() => this.mapSearching.set(false)))
      .subscribe({
        next: (result) => {
          this.localMapResults.set(result.local.items);
          this.workshopResults.set(result.workshop?.items ?? []);
          if (!result.workshop) {
            this.mapMessage.set('Workshop-Suche übersprungen (Web-API-Key fehlt oder Fehler).');
          }
        },
        error: () => this.error.set('Map-Suche fehlgeschlagen.'),
      });
  }

  inferPreset(mapName: string): string | null {
    const normalized = mapName.trim().toLowerCase();
    if (!normalized) return null;
    return (
      this.modeCatalog()?.presets.find((preset) =>
        preset.mapPrefixes.some((prefix) => normalized.startsWith(prefix.toLowerCase())),
      )?.id ?? null
    );
  }

  presetName(presetId: string | null): string {
    if (!presetId) return 'Kein Preset erkannt';
    return this.modeCatalog()?.presets.find((preset) => preset.id === presetId)?.name ?? presetId;
  }

  queueMap(mapName: string, workshopId: string | null, presetId: string | null): void {
    const server = this.selectedServer();
    if (!server || this.mapQueueing()) return;

    const preset = presetId ?? this.inferPreset(mapName) ?? 'classic';
    this.error.set('');
    this.mapQueueing.set(mapName);
    this.api
      .scheduleCs2MapByMap(server.id, {
        presetId: preset,
        mapName,
        workshopId,
        delaySeconds: this.mapChangeDelay(),
      })
      .pipe(finalize(() => this.mapQueueing.set('')))
      .subscribe({
        next: (state) => {
          this.mapChangeState.set(state);
          this.mapMessage.set(state.message);
        },
        error: (error) =>
          this.error.set(error.error?.detail ?? 'Map konnte nicht eingereiht werden.'),
      });
  }

  cancelMapChange(): void {
    const server = this.selectedServer();
    if (!server) return;

    this.error.set('');
    this.api.cancelCs2MapChange(server.id).subscribe({
      next: (state) => {
        this.mapChangeState.set(state);
        this.mapMessage.set(state.message);
      },
      error: (error) =>
        this.error.set(error.error?.detail ?? 'Map-Wechsel konnte nicht abgebrochen werden.'),
    });
  }

  metaPlugins(): Cs2LoadedPlugin[] {
    return this.pluginState()?.plugins.filter((plugin) => plugin.loader === 'metamod') ?? [];
  }

  cssPlugins(): Cs2LoadedPlugin[] {
    return this.pluginState()?.plugins.filter((plugin) => plugin.loader === 'counterstrikesharp') ?? [];
  }

  notLoadedCssPlugins(): string[] {
    const loaded = new Set(this.cssPlugins().map((plugin) => plugin.name.toLowerCase()));
    return (this.pluginState()?.installedCssPlugins ?? []).filter(
      (name) => !loaded.has(name.toLowerCase()),
    );
  }

  runPluginAction(action: 'load' | 'unload' | 'reload', name: string): void {
    const server = this.selectedServer();
    if (!server || this.pluginActioning()) return;

    this.error.set('');
    this.pluginActioning.set(`${action}:${name}`);
    this.api
      .runCs2PluginAction(server.id, { action, name })
      .pipe(finalize(() => this.pluginActioning.set('')))
      .subscribe({
        next: (state) => this.pluginState.set(state),
        error: (error) =>
          this.error.set(error.error?.detail ?? 'Plugin-Aktion konnte nicht ausgeführt werden.'),
      });
  }

  private loadPluginState(): void {
    const server = this.selectedServer();
    if (!server || server.templateId !== 'counter-strike-2') {
      this.pluginState.set(null);
      return;
    }

    this.api.cs2Plugins(server.id).subscribe({
      next: (state) => this.pluginState.set(state),
      error: () => {
        // Best effort: plugin state is optional context for the operator.
      },
    });
  }

  private loadModeCatalog(): void {
    this.api.cs2ModeCatalog().subscribe({
      next: (catalog) => this.modeCatalog.set(catalog),
      error: () => {
        // The catalog only powers preset names and prefix inference.
      },
    });
  }

  private loadMapChangeState(): void {
    const server = this.selectedServer();
    if (!server || server.templateId !== 'counter-strike-2') {
      this.mapChangeState.set(null);
      return;
    }

    this.api.cs2MapChange(server.id).subscribe({
      next: (state) => this.mapChangeState.set(state),
      error: () => {
        // Best effort: the map-change state is optional context for the operator.
      },
    });
  }

  private loadServers(): void {
    this.loading.set(true);
    this.api
      .servers()
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (servers) => {
          this.servers.set(servers);
          const currentId = this.selectedServerId();
          const selection =
            servers.find((server) => server.id === currentId) ??
            servers.find((server) => server.templateId === 'counter-strike-2') ??
            servers[0] ??
            null;
          this.selectedServerId.set(selection?.id ?? '');
          this.selectedServer.set(selection);
          if (selection) {
            this.refreshSelectedServer(true);
            this.loadBasicConfiguration(selection.id);
          }
        },
        error: () => this.error.set('Vorhandene Server konnten nicht geladen werden.'),
      });
  }

  private refreshSelectedServer(silent = false): void {
    const serverId = this.selectedServerId();
    if (!serverId || this.actioning()) return;

    this.api.server(serverId).subscribe({
      next: (server) => {
        this.selectedServer.set(server);
        this.servers.update((servers) =>
          servers.map((item) => (item.id === server.id ? server : item)),
        );
      },
      error: (error) => {
        if (!silent)
          this.error.set(error.error?.detail ?? 'Serverstatus konnte nicht geladen werden.');
      },
    });
  }

  private loadBasicConfiguration(serverId: string): void {
    const server = this.servers().find((item) => item.id === serverId);
    if (server?.templateId !== 'counter-strike-2') {
      this.basicConfiguration.set(null);
      return;
    }

    this.api.cs2BasicConfiguration(serverId).subscribe({
      next: (state) => {
        this.basicConfiguration.set(state.configuration);
        this.configMessage.set(state.message);
      },
      error: (error) =>
        this.error.set(error.error?.detail ?? 'Basic-Konfiguration konnte nicht geladen werden.'),
    });
  }

  private addCommandEntry(command: string, result: ConsoleCommandResult): void {
    this.commandHistory.update((history) =>
      [
        {
          command,
          output:
            result.output?.trim() ||
            'Command wurde gesendet. Der Server hat keine Textausgabe zurückgegeben.',
          transport: result.transport,
          failed: false,
          createdAt: new Date(),
        },
        ...history,
      ].slice(0, 50),
    );
  }

  private actionLabel(action: 'start' | 'stop' | 'restart'): string {
    return action === 'start' ? 'gestartet' : action === 'stop' ? 'gestoppt' : 'neu gestartet';
  }
}
