import { DatePipe } from '@angular/common';
import { Component, inject, OnDestroy, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize, forkJoin } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { Cs2ModeCatalog, Cs2ModePreset, Cs2ModeProfile, Cs2ModeState, GameServer, ServerEvent } from '../../core/models';
import { RealtimeService } from '../../core/realtime.service';

@Component({
  selector: 'app-server-detail',
  imports: [RouterLink, DatePipe],
  templateUrl: './server-detail.component.html'
})
export class ServerDetailComponent implements OnDestroy {
  private readonly api = inject(ApiService);
  private readonly realtime = inject(RealtimeService);
  private readonly router = inject(Router);
  private readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id')!;
  private readonly refreshTimer: ReturnType<typeof setInterval>;
  readonly server = signal<GameServer | null>(null);
  readonly logs = signal<ServerEvent[]>([]);
  readonly logsLoading = signal(true);
  readonly logsError = signal('');
  readonly tab = signal<'overview' | 'modes' | 'console' | 'players'>('overview');
  readonly command = signal('');
  readonly progress = signal<{ percent: number; stage: string; message: string } | null>(null);
  readonly error = signal('');
  readonly publicationPort = signal<number | null>(null);
  readonly publicationSaving = signal(false);
  readonly copied = signal('');
  readonly modeCatalog = signal<Cs2ModeCatalog | null>(null);
  readonly modeState = signal<Cs2ModeState | null>(null);
  readonly selectedPresetId = signal('');
  readonly modeMapName = signal('');
  readonly modeWorkshopId = signal('');
  readonly modeBotQuota = signal(0);
  readonly modeBotDifficulty = signal(1);
  readonly modeInstallPackages = signal(true);
  readonly modeOverrides = signal<Record<string, string>>({});
  readonly modeSaving = signal(false);
  readonly packageQueueing = signal('');
  readonly actioning = signal('');

  constructor() {
    this.load();
    void this.realtime.connect(this.id, {
      consoleLine: line => this.logs.update(logs => [...logs, { id: 0, serverId: this.id, type: 'ConsoleOutput', ...line }].slice(-500)),
      statusChanged: status => {
        this.server.update(server => server ? { ...server, status } : server);
        this.loadServer();
        this.loadLogs();
      },
      installationProgress: progress => {
        this.progress.set(progress);
        this.logs.update(logs => [...logs, {
          id: 0,
          serverId: this.id,
          type: 'InstallationProgress',
          message: progress.message,
          dataJson: JSON.stringify(progress),
          occurredAt: new Date().toISOString()
        }].slice(-500));
      }
    }).catch(() => this.error.set('Live updates are temporarily unavailable.'));
    this.refreshTimer = setInterval(() => this.loadServer(), 5000);
  }

  ngOnDestroy(): void {
    clearInterval(this.refreshTimer);
    void this.realtime.disconnect();
  }

  load(): void {
    // Render the server as soon as its request completes. Logs are optional supporting data and
    // must never keep the complete detail page in its loading state.
    this.loadServer(true);
    this.loadLogs();
  }

  private loadLogs(): void {
    this.logsLoading.set(true);
    this.logsError.set('');
    this.api.logs(this.id).subscribe({
      next: logs => {
        this.logs.set([...logs].reverse());
        this.logsLoading.set(false);
      },
      error: error => {
        this.logsLoading.set(false);
        this.logsError.set(error.error?.detail ?? 'Recent activity could not be loaded. Live server controls remain available.');
      }
    });
  }

  loadCs2Modes(): void {
    forkJoin({ catalog: this.api.cs2ModeCatalog(), state: this.api.cs2Mode(this.id) }).subscribe({
      next: result => {
        this.modeCatalog.set(result.catalog);
        this.modeState.set(result.state);
        const active = result.state.profiles.find(profile => profile.id === result.state.activeProfileId);
        if (active) {
          this.selectProfile(active);
        } else if (!this.selectedPresetId() && result.catalog.presets.length) {
          this.selectPreset(result.catalog.presets[0].id);
          this.modeMapName.set(this.server()?.settings?.['initialMap'] || 'de_mirage');
        }
      },
      error: error => this.error.set(error.error?.detail ?? 'CS2 mode presets could not be loaded.')
    });
  }

  selectedPreset(): Cs2ModePreset | null {
    return this.modeCatalog()?.presets.find(preset => preset.id === this.selectedPresetId()) ?? null;
  }

  selectPreset(presetId: string): void {
    const preset = this.modeCatalog()?.presets.find(item => item.id === presetId);
    if (!preset) return;
    this.selectedPresetId.set(preset.id);
    this.modeOverrides.set(Object.fromEntries(
      preset.settings.filter(setting => setting.editable).map(setting => [setting.key, setting.defaultValue])
    ));
  }

  selectProfile(profile: Cs2ModeProfile): void {
    this.selectPreset(profile.presetId);
    this.modeMapName.set(profile.mapName);
    this.modeWorkshopId.set(profile.workshopId ?? '');
    this.modeBotQuota.set(profile.botQuota);
    this.modeBotDifficulty.set(profile.botDifficulty);
    this.modeOverrides.update(values => ({ ...values, ...profile.overrides }));
  }

  updateModeText(field: 'map' | 'workshop', event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    if (field === 'map') this.modeMapName.set(value);
    else this.modeWorkshopId.set(value);
  }

  updateModeNumber(field: 'bots' | 'difficulty', event: Event): void {
    const value = Number((event.target as HTMLInputElement).value);
    if (field === 'bots') this.modeBotQuota.set(value);
    else this.modeBotDifficulty.set(value);
  }

  updateModeOverride(key: string, event: Event): void {
    this.modeOverrides.update(values => ({ ...values, [key]: (event.target as HTMLInputElement | HTMLSelectElement).value }));
  }

  applyModePreset(): void {
    const preset = this.selectedPreset();
    if (!preset) return;
    this.error.set('');
    this.modeSaving.set(true);
    this.api.applyCs2Mode(this.id, {
      presetId: preset.id,
      mapName: this.modeMapName().trim(),
      workshopId: this.modeWorkshopId().trim() || null,
      botQuota: this.modeBotQuota(),
      botDifficulty: this.modeBotDifficulty(),
      installRecommendedPackages: this.modeInstallPackages(),
      overrides: this.modeOverrides()
    }).subscribe({
      next: result => {
        this.modeState.set(result.state);
        this.modeSaving.set(false);
        if (result.queuedPackageIds.length) {
          this.progress.set({ percent: 0, stage: 'queued', message: `Queued ${result.queuedPackageIds.length} managed package(s).` });
        }
      },
      error: error => {
        this.error.set(error.error?.detail ?? 'The map preset could not be applied.');
        this.modeSaving.set(false);
      }
    });
  }

  installPackage(packageId: string): void {
    this.error.set('');
    this.packageQueueing.set(packageId);
    this.api.installCs2Package(this.id, packageId).subscribe({
      next: () => {
        this.packageQueueing.set('');
        this.progress.set({ percent: 0, stage: 'queued', message: `Queued ${packageId} and its dependencies.` });
      },
      error: error => {
        this.error.set(error.error?.detail ?? 'The package could not be queued.');
        this.packageQueueing.set('');
      }
    });
  }

  loadServer(loadModes = false): void {
    this.api.server(this.id).subscribe({
      next: server => {
        this.server.set(server);
        if (this.publicationPort() === null) this.publicationPort.set(server.publication.publicPort);
        if (loadModes && server.templateId === 'counter-strike-2') this.loadCs2Modes();
      },
      error: error => this.error.set(error.error?.detail ?? 'The server could not be loaded.')
    });
  }

  action(action: 'start' | 'stop' | 'restart' | 'kill' | 'update'): void {
    this.error.set('');
    this.actioning.set(action);
    if (action === 'update') {
      this.progress.set({ percent: 0, stage: 'queued', message: 'Server update queued…' });
    }
    this.api.serverAction(this.id, action).pipe(finalize(() => this.actioning.set(''))).subscribe({
      next: () => {
        this.loadServer();
        this.loadLogs();
      },
      error: error => this.error.set(error.error?.detail ?? 'The server action failed.')
    });
  }

  deleteServer(): void {
    const server = this.server();
    if (!server || !window.confirm(`Delete "${server.name}" and all files in its managed server directory? This cannot be undone.`)) {
      return;
    }

    this.error.set('');
    this.actioning.set('delete');
    this.api.deleteServer(this.id).pipe(finalize(() => this.actioning.set(''))).subscribe({
      next: () => void this.router.navigate(['/servers']),
      error: error => this.error.set(error.error?.detail ?? 'The server could not be deleted.')
    });
  }

  updateCommand(event: Event): void {
    this.command.set((event.target as HTMLInputElement).value);
  }

  sendCommand(): void {
    const command = this.command().trim();
    if (!command) return;
    this.api.sendCommand(this.id, command).subscribe({
      next: () => this.command.set(''),
      error: error => this.error.set(error.error?.detail ?? 'The command could not be sent.')
    });
  }

  updatePublicationPort(event: Event): void {
    this.publicationPort.set(Number((event.target as HTMLInputElement).value));
  }

  savePublication(published: boolean): void {
    const publicPort = this.publicationPort();
    if (!publicPort || publicPort < 1 || publicPort > 65535) {
      this.error.set('The public port must be between 1 and 65535.');
      return;
    }

    this.error.set('');
    this.publicationSaving.set(true);
    this.api.updatePublication(this.id, published, publicPort).subscribe({
      next: publication => {
        this.server.update(server => server ? { ...server, publication } : server);
        this.publicationSaving.set(false);
      },
      error: error => {
        this.error.set(error.error?.detail ?? 'The guest publication could not be updated.');
        this.publicationSaving.set(false);
      }
    });
  }

  copy(value: string): void {
    void this.copyText(value).then(() => {
      this.copied.set(value);
      setTimeout(() => this.copied.set(''), 1800);
    });
  }

  private async copyText(value: string): Promise<void> {
    if (navigator.clipboard?.writeText) {
      try {
        await navigator.clipboard.writeText(value);
        return;
      } catch {
        // Fall through for HTTP LAN access without the secure Clipboard API.
      }
    }

    const input = document.createElement('textarea');
    input.value = value;
    input.style.position = 'fixed';
    input.style.opacity = '0';
    document.body.appendChild(input);
    input.select();
    document.execCommand('copy');
    input.remove();
  }

  consoleLogs(): ServerEvent[] {
    return this.logs().filter(log =>
      log.type === 'ConsoleOutput' ||
      log.type === 'InstallationProgress' ||
      log.type === 'ServerUpdateStarted' ||
      log.type === 'ServerUpdateFailed' ||
      log.type === 'ServerStartRequested' ||
      log.type === 'ServerStartProgress');
  }

  formatBytes(bytes: number): string {
    return bytes ? `${(bytes / 1024 / 1024).toFixed(0)} MB` : '0 MB';
  }
}
