import { Component, DestroyRef, inject, signal } from '@angular/core';
import { finalize } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { ConsoleCommandResult, Cs2BasicConfiguration, GameServer } from '../../core/models';

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
  readonly actioning = signal('');
  readonly command = signal('');
  readonly commandRunning = signal(false);
  readonly commandHistory = signal<CommandEntry[]>([]);
  readonly basicConfiguration = signal<Cs2BasicConfiguration | null>(null);
  readonly configSaving = signal(false);
  readonly configMessage = signal('');
  readonly error = signal('');

  constructor() {
    this.loadServers();
    this.refreshHandle = setInterval(() => this.refreshSelectedServer(true), 3000);
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
    this.error.set('');
    this.refreshSelectedServer();
    this.loadBasicConfiguration(serverId);
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
