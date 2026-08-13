import { DatePipe } from '@angular/common';
import { Component, inject, OnDestroy, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize, forkJoin } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { ConsoleCommandResult, Cs2LiveControlState, Cs2LiveSetting, Cs2ModeCatalog, Cs2ModePreset, Cs2ModeProfile, Cs2ModeState, Cs2QuickAction, Cs2WorkshopMap, GameServer, ServerEvent, ServerSelfTestResult } from '../../core/models';
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
  readonly tab = signal<'overview' | 'modes' | 'control' | 'console' | 'players'>('overview');
  readonly command = signal('');
  readonly commandAction = signal('');
  readonly selfTestResult = signal<ServerSelfTestResult | null>(null);
  readonly progress = signal<{ percent: number; stage: string; message: string } | null>(null);
  readonly error = signal('');
  readonly publicationPort = signal<number | null>(null);
  readonly publicationSaving = signal(false);
  readonly copied = signal('');
  readonly modeCatalog = signal<Cs2ModeCatalog | null>(null);
  readonly modeState = signal<Cs2ModeState | null>(null);
  readonly modeStateRefreshing = signal(false);
  readonly selectedPresetId = signal('');
  readonly modeMapName = signal('');
  readonly modeWorkshopId = signal('');
  readonly modeBotQuota = signal(0);
  readonly modeBotDifficulty = signal(1);
  readonly modeInstallPackages = signal(true);
  readonly modeOverrides = signal<Record<string, string>>({});
  readonly modeSaving = signal(false);
  readonly packageQueueing = signal('');
  readonly workshopQuery = signal('surf_');
  readonly workshopResults = signal<Cs2WorkshopMap[]>([]);
  readonly workshopTotal = signal(0);
  readonly workshopSearching = signal(false);
  readonly workshopAdding = signal('');
  readonly workshopKey = signal('');
  readonly workshopKeySaving = signal(false);
  readonly workshopMessage = signal('');
  readonly actioning = signal('');
  readonly liveControl = signal<Cs2LiveControlState | null>(null);
  readonly liveValues = signal<Record<string, string>>({});
  readonly liveLoading = signal(false);
  readonly liveSaving = signal(false);
  readonly liveAction = signal('');
  readonly liveMessage = signal('');
  readonly liveMap = signal('de_mirage');
  readonly gsltToken = signal('');
  readonly gsltSaving = signal(false);

  constructor() {
    this.load();
    void this.realtime.connect(this.id, {
      consoleLine: line => this.logs.update(logs => [...logs, { id: 0, serverId: this.id, type: 'ConsoleOutput', ...line }].slice(-500)),
      statusChanged: status => {
        this.server.update(server => server ? { ...server, status } : server);
        this.liveControl.update(control => control ? { ...control, running: status === 'Running' } : control);
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

  refreshCs2ModeState(showError = false): void {
    if (this.modeStateRefreshing()) return;
    this.modeStateRefreshing.set(true);
    this.api.cs2Mode(this.id).pipe(finalize(() => this.modeStateRefreshing.set(false))).subscribe({
      next: state => this.modeState.set(state),
      error: error => {
        if (showError) {
          this.error.set(error.error?.detail ?? 'The Workshop installation state could not be refreshed.');
        }
      }
    });
  }

  selectTab(tab: 'overview' | 'modes' | 'control' | 'console' | 'players'): void {
    this.tab.set(tab);
    if (tab === 'control' && !this.liveControl()) {
      this.loadCs2Control();
    }
  }

  loadCs2Control(): void {
    this.liveLoading.set(true);
    this.liveMessage.set('');
    this.api.cs2LiveControl(this.id).pipe(finalize(() => this.liveLoading.set(false))).subscribe({
      next: state => {
        this.liveControl.set(state);
        this.liveValues.set({ ...state.values });
        this.liveMessage.set(state.liveReadMessage);
        const currentMap = this.server()?.currentMap || this.server()?.settings?.['initialMap'];
        if (currentMap) this.liveMap.set(currentMap);
      },
      error: error => this.error.set(error.error?.detail ?? 'The CS2 live configuration could not be loaded.')
    });
  }

  liveGroups(): string[] {
    return [...new Set(this.liveControl()?.settings.map(setting => setting.group) ?? [])];
  }

  liveSettingsFor(group: string): Cs2LiveSetting[] {
    return this.liveControl()?.settings.filter(setting => setting.group === group) ?? [];
  }

  liveActionGroups(): string[] {
    return [...new Set(this.liveControl()?.actions.map(action => action.group) ?? [])];
  }

  liveActionsFor(group: string): Cs2QuickAction[] {
    return this.liveControl()?.actions.filter(action => action.group === group) ?? [];
  }

  updateLiveValue(key: string, event: Event): void {
    const value = (event.target as HTMLInputElement | HTMLSelectElement).value;
    this.liveValues.update(values => ({ ...values, [key]: value }));
  }

  applyLiveConfiguration(): void {
    this.error.set('');
    this.liveMessage.set('');
    this.liveSaving.set(true);
    this.api.applyCs2LiveControl(this.id, this.liveValues()).pipe(finalize(() => this.liveSaving.set(false))).subscribe({
      next: result => {
        this.liveValues.set({ ...result.values });
        this.liveMessage.set(result.message);
        this.appendConsoleMessage(result.message, 'ConfigurationChanged');
        if (result.output) this.appendConsoleMessage(result.output, 'ConsoleOutput');
      },
      error: error => this.error.set(error.error?.detail ?? 'The live configuration could not be applied.')
    });
  }

  runLiveAction(action: Cs2QuickAction): void {
    this.executeLiveAction(action.id, action.label);
  }

  changeLiveMap(): void {
    const map = this.liveMap().trim();
    if (!map) return;
    this.executeLiveAction('change-map', `Change map to ${map}`, map);
  }

  updateLiveMap(event: Event): void {
    this.liveMap.set((event.target as HTMLInputElement).value);
  }

  updateGsltToken(event: Event): void {
    this.gsltToken.set((event.target as HTMLInputElement).value.trim());
  }

  saveGslt(): void {
    const token = this.gsltToken();
    if (!token) return;
    this.error.set('');
    this.liveMessage.set('');
    this.gsltSaving.set(true);
    this.api.configureCs2Gslt(this.id, token).pipe(finalize(() => this.gsltSaving.set(false))).subscribe({
      next: result => {
        this.gsltToken.set('');
        this.liveControl.update(state => state ? { ...state, gslt: result.state } : state);
        this.liveMessage.set(result.message);
        this.appendConsoleMessage(result.message, 'ConfigurationChanged');
      },
      error: error => this.error.set(error.error?.detail ?? 'The Steam game-server token could not be saved.')
    });
  }

  selectedPreset(): Cs2ModePreset | null {
    return this.modeCatalog()?.presets.find(preset => preset.id === this.selectedPresetId()) ?? null;
  }

  activeModeProfile(): Cs2ModeProfile | null {
    const state = this.modeState();
    return state?.profiles.find(profile => profile.id === state.activeProfileId) ?? null;
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

  updateWorkshopQuery(event: Event): void {
    this.workshopQuery.set((event.target as HTMLInputElement).value);
  }

  updateWorkshopKey(event: Event): void {
    this.workshopKey.set((event.target as HTMLInputElement).value.trim());
  }

  saveWorkshopKey(): void {
    const key = this.workshopKey();
    if (!key) return;
    this.error.set('');
    this.workshopMessage.set('');
    this.workshopKeySaving.set(true);
    this.api.configureCs2WorkshopKey(this.id, key).pipe(finalize(() => this.workshopKeySaving.set(false))).subscribe({
      next: result => {
        this.workshopKey.set('');
        this.modeState.update(state => state ? { ...state, workshop: result.state } : state);
        this.workshopMessage.set(result.message);
      },
      error: error => this.error.set(error.error?.detail ?? 'The Steam Workshop key could not be saved.')
    });
  }

  searchWorkshop(): void {
    const query = this.workshopQuery().trim();
    if (query.length < 2) return;
    this.error.set('');
    this.workshopMessage.set('');
    this.workshopSearching.set(true);
    this.api.searchCs2Workshop(this.id, query).pipe(finalize(() => this.workshopSearching.set(false))).subscribe({
      next: result => {
        this.workshopResults.set(result.items);
        this.workshopTotal.set(result.total);
        this.workshopMessage.set(result.items.length
          ? `Found ${result.total.toLocaleString()} matching Workshop item(s). Showing compatible CS2 map files first.`
          : 'Steam returned no selectable CS2 maps. Removed, private and collection items are filtered out.');
      },
      error: error => this.error.set(error.error?.detail ?? 'Steam Workshop search failed.')
    });
  }

  addWorkshopMap(map: Cs2WorkshopMap): void {
    const inferredPreset = this.modeCatalog()?.presets.find(preset =>
      preset.mapPrefixes.some(prefix => map.mapName.toLowerCase().startsWith(prefix.toLowerCase())));
    if (inferredPreset && inferredPreset.id !== this.selectedPresetId()) {
      this.selectPreset(inferredPreset.id);
    }
    this.modeMapName.set(map.mapName);
    this.modeWorkshopId.set(map.publishedFileId);
    this.applyModePreset(map);
  }

  formatWorkshopSize(bytes: number): string {
    if (!bytes) return 'Size unknown';
    return bytes >= 1024 * 1024 * 1024
      ? `${(bytes / (1024 * 1024 * 1024)).toFixed(1)} GB`
      : `${Math.max(1, Math.round(bytes / (1024 * 1024)))} MB`;
  }

  formatWorkshopCount(value: number): string {
    return new Intl.NumberFormat(undefined, { notation: 'compact', maximumFractionDigits: 1 }).format(value);
  }

  applyModePreset(workshopMap: Cs2WorkshopMap | null = null): void {
    const preset = this.selectedPreset();
    if (!preset) return;
    this.error.set('');
    this.modeSaving.set(true);
    if (workshopMap) this.workshopAdding.set(workshopMap.publishedFileId);
    this.api.applyCs2Mode(this.id, {
      presetId: preset.id,
      mapName: this.modeMapName().trim(),
      workshopId: this.modeWorkshopId().trim() || null,
      botQuota: this.modeBotQuota(),
      botDifficulty: this.modeBotDifficulty(),
      installRecommendedPackages: this.modeInstallPackages(),
      overrides: this.modeOverrides()
    }).pipe(finalize(() => {
      this.modeSaving.set(false);
      this.workshopAdding.set('');
    })).subscribe({
      next: result => {
        this.modeState.set(result.state);
        if (result.queuedPackageIds.length) {
          this.progress.set({ percent: 0, stage: 'queued', message: `Queued ${result.queuedPackageIds.length} managed package(s).` });
        } else if (workshopMap) {
          this.workshopMessage.set(`Added ${workshopMap.title}. Start CS2 to download and host the latest Workshop version.`);
        }
      },
      error: error => {
        this.error.set(error.error?.detail ?? 'The map preset could not be applied.');
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
        this.liveControl.update(control => control ? {
          ...control,
          running: server.status === 'Running' && server.process.isRunning
        } : control);
        if (this.publicationPort() === null) this.publicationPort.set(server.publication.publicPort);
        if (loadModes && server.templateId === 'counter-strike-2') this.loadCs2Modes();
        else if (server.templateId === 'counter-strike-2' && this.modeState() &&
          (this.tab() === 'modes' ||
            (server.status === 'Running' && this.activeModeProfile()?.workshopInstallState === 'pending'))) {
          this.refreshCs2ModeState();
        }
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
    this.executeCommand('custom', command, () => this.command.set(''));
  }

  quickCommand(label: string, command: string): void {
    this.executeCommand(label, command);
  }

  runSelfTest(): void {
    this.error.set('');
    this.selfTestResult.set(null);
    this.commandAction.set('self-test');
    this.api.selfTest(this.id).pipe(finalize(() => this.commandAction.set(''))).subscribe({
      next: result => {
        this.selfTestResult.set(result);
        this.appendConsoleMessage(result.message, result.passed ? 'CommandSelfTestPassed' : 'CommandSelfTestFailed');
        if (result.output) this.appendConsoleMessage(result.output, 'ConsoleOutput');
      },
      error: error => this.error.set(error.error?.detail ?? 'The server self-test failed.')
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
      log.type === 'ServerStartProgress' ||
      log.type === 'ConsoleCommand' ||
      log.type.startsWith('CommandSelfTest'));
  }

  private executeCommand(label: string, command: string, completed?: () => void): void {
    this.error.set('');
    this.commandAction.set(label);
    this.api.sendCommand(this.id, command).pipe(finalize(() => this.commandAction.set(''))).subscribe({
      next: result => {
        completed?.();
        this.appendCommandResult(command, result);
      },
      error: error => this.error.set(error.error?.detail ?? `The '${label}' command could not be executed.`)
    });
  }

  private executeLiveAction(actionId: string, label: string, value: string | null = null): void {
    this.error.set('');
    this.liveMessage.set('');
    this.liveAction.set(actionId);
    this.api.runCs2Action(this.id, actionId, value).pipe(finalize(() => this.liveAction.set(''))).subscribe({
      next: result => {
        const message = `${label} executed successfully.`;
        this.liveMessage.set(message);
        this.appendConsoleMessage(`> ${label} (${result.transport})`, 'ConsoleCommand');
        if (result.output) this.appendConsoleMessage(result.output, 'ConsoleOutput');
      },
      error: error => this.error.set(error.error?.detail ?? `The '${label}' action could not be executed.`)
    });
  }

  private appendCommandResult(command: string, result: ConsoleCommandResult): void {
    this.appendConsoleMessage(`> ${command} (${result.transport})`, 'ConsoleCommand');
    if (result.output) this.appendConsoleMessage(result.output, 'ConsoleOutput');
  }

  private appendConsoleMessage(message: string, type: string): void {
    this.logs.update(logs => [...logs, {
      id: 0,
      serverId: this.id,
      type,
      message,
      dataJson: null,
      occurredAt: new Date().toISOString()
    }].slice(-500));
  }

  formatBytes(bytes: number): string {
    return bytes ? `${(bytes / 1024 / 1024).toFixed(0)} MB` : '0 MB';
  }
}
