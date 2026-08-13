import { DatePipe } from '@angular/common';
import { Component, inject, OnDestroy, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize, forkJoin } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { ConsoleCommandResult, Cs2AmmoMode, Cs2CombatMode, Cs2HudMode, Cs2LiveControlState, Cs2LiveSetting, Cs2ModeCatalog, Cs2ModePreset, Cs2ModeProfile, Cs2ModeState, Cs2QuickAction, Cs2RespawnMode, Cs2WorkshopMap, GameServer, ServerEvent, ServerSelfTestResult } from '../../core/models';
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
  private readonly countdownTimer: ReturnType<typeof setInterval>;
  private liveRefreshInFlight = false;
  private liveRefreshQueued = false;
  private lastLiveRefreshAt = 0;
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
  readonly modeCombat = signal<Cs2CombatMode>('team');
  readonly modeAmmo = signal<Cs2AmmoMode>('standard');
  readonly modeHud = signal<Cs2HudMode>('hidden');
  readonly modeRespawn = signal<Cs2RespawnMode>('round');
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
  readonly liveObservedValues = signal<Record<string, string>>({});
  readonly liveDirtyKeys = signal<string[]>([]);
  readonly liveLoading = signal(false);
  readonly liveSaving = signal(false);
  readonly liveAction = signal('');
  readonly liveMessage = signal('');
  readonly liveEditorView = signal<'recommended' | 'all'>('recommended');
  readonly liveQuery = signal('');
  readonly activeLiveGroup = signal('Round & match');
  readonly activeActionGroup = signal('Round');
  readonly nextMapProfileId = signal('');
  readonly nextMapDelaySeconds = signal(60);
  readonly mapChangeAction = signal('');
  readonly mapClock = signal(Date.now());
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
    this.countdownTimer = setInterval(() => this.mapClock.set(Date.now()), 1000);
  }

  ngOnDestroy(): void {
    clearInterval(this.refreshTimer);
    clearInterval(this.countdownTimer);
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
        if (!this.nextMapProfileId() && active) this.nextMapProfileId.set(active.id);
        else if (!this.nextMapProfileId() && result.state.profiles.length) this.nextMapProfileId.set(result.state.profiles[0].id);
        if (active) {
          this.selectProfile(active);
        } else if (!this.selectedPresetId() && result.catalog.presets.length) {
          this.selectPreset(result.catalog.presets[0].id);
          this.modeMapName.set(this.server()?.settings?.['initialMap'] || 'de_mirage');
        }
        this.normalizeLiveGroup();
      },
      error: error => this.error.set(error.error?.detail ?? 'CS2 mode presets could not be loaded.')
    });
  }

  refreshCs2ModeState(showError = false): void {
    if (this.modeStateRefreshing()) return;
    this.modeStateRefreshing.set(true);
    this.api.cs2Mode(this.id).pipe(finalize(() => this.modeStateRefreshing.set(false))).subscribe({
      next: state => {
        this.modeState.set(state);
        this.normalizeLiveGroup();
      },
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

  loadCs2Control(preserveMessage = false, silent = false): void {
    if (this.liveRefreshInFlight) {
      this.liveRefreshQueued = true;
      return;
    }
    this.liveRefreshInFlight = true;
    if (!silent) this.liveLoading.set(true);
    if (!preserveMessage) this.liveMessage.set('');
    const dirtyKeys = new Set(this.liveDirtyKeys());
    const stagedValues = this.liveValues();
    this.api.cs2LiveControl(this.id).pipe(finalize(() => {
      this.liveRefreshInFlight = false;
      if (!silent) this.liveLoading.set(false);
      if (this.liveRefreshQueued) {
        this.liveRefreshQueued = false;
        queueMicrotask(() => this.loadCs2Control(true, true));
      }
    })).subscribe({
      next: state => {
        this.liveControl.set(state);
        this.liveObservedValues.set({ ...state.values });
        this.liveValues.set(Object.fromEntries(Object.entries(state.values).map(([key, value]) =>
          [key, dirtyKeys.has(key) ? stagedValues[key] ?? value : value])));
        this.liveDirtyKeys.set([...dirtyKeys].filter(key => key in state.values));
        this.lastLiveRefreshAt = Date.now();
        if (!preserveMessage) this.liveMessage.set(state.liveReadMessage);
        this.normalizeLiveGroup();
      },
      error: error => {
        if (!silent) this.error.set(error.error?.detail ?? 'The CS2 live configuration could not be loaded.');
      }
    });
  }

  liveGroups(): string[] {
    return [...new Set(this.liveSettingsInView().map(setting => setting.group))];
  }

  displayedLiveGroups(): string[] {
    const groups = this.liveGroups();
    if (this.liveQuery().trim()) {
      return groups.filter(group => this.liveSettingsFor(group).length > 0);
    }

    const active = groups.includes(this.activeLiveGroup()) ? this.activeLiveGroup() : groups[0];
    return active ? [active] : [];
  }

  liveSettingsFor(group: string): Cs2LiveSetting[] {
    const query = this.liveQuery().trim().toLocaleLowerCase();
    return this.liveSettingsInView().filter(setting =>
      setting.group === group &&
      (!query || `${setting.label} ${setting.key} ${setting.description} ${setting.group}`.toLocaleLowerCase().includes(query)));
  }

  liveSettingCount(group: string): number {
    return this.liveSettingsInView().filter(setting => setting.group === group).length;
  }

  liveSearchResultCount(): number {
    return this.displayedLiveGroups().reduce((total, group) => total + this.liveSettingsFor(group).length, 0);
  }

  livePresetName(): string {
    return this.modeCatalog()?.presets.find(preset => preset.id === this.activeModeProfile()?.presetId)?.name ?? 'this server';
  }

  liveGroupDescription(group: string): string {
    return ({
      'Round & match': 'Warmup, round duration, buy time and the overall match flow.',
      'Teams & bots': 'Team balance, player interaction and how bots join the match.',
      'Combat & damage': 'Enemy damage multipliers and special hit rules. Normal team modes use 1.0 for every multiplier.',
      'Movement & physics': 'Gravity, acceleration, bunnyhop behavior and maximum movement speed.',
      'Admin playground': 'Private practice tools such as cheats, ammunition, respawns and endless rounds.'
    } as Record<string, string>)[group] ?? 'Runtime values for the running CS2 server.';
  }

  liveGroupIcon(group: string): string {
    return ({
      'Round & match': '◷',
      'Teams & bots': 'VS',
      'Combat & damage': 'HP',
      'Movement & physics': '↗',
      'Admin playground': '⚙'
    } as Record<string, string>)[group] ?? '•';
  }

  liveOptionLabel(setting: Cs2LiveSetting, option: string): string {
    if (setting.key === 'bot_quota_mode') {
      return ({ normal: 'Manual bot count', fill: 'Fill empty player slots', match: 'Match the human player count' } as Record<string, string>)[option] ?? option;
    }
    if (setting.key === 'sv_infinite_ammo') {
      return ({ '0': 'Off', '1': 'Infinite magazine', '2': 'Infinite reserve ammunition' } as Record<string, string>)[option] ?? option;
    }
    return option;
  }

  setLiveEditorView(view: 'recommended' | 'all'): void {
    this.liveEditorView.set(view);
    this.normalizeLiveGroup();
  }

  selectLiveGroup(group: string): void {
    this.activeLiveGroup.set(group);
    this.liveQuery.set('');
  }

  updateLiveQuery(event: Event): void {
    this.liveQuery.set((event.target as HTMLInputElement).value);
  }

  clearLiveQuery(): void {
    this.liveQuery.set('');
  }

  private liveSettingsInView(): Cs2LiveSetting[] {
    const settings = this.liveControl()?.settings ?? [];
    if (this.liveEditorView() === 'all') return settings;

    const presetId = this.activeModeProfile()?.presetId ?? 'classic';
    const relevantGroups = presetId === 'surf' || presetId === 'kz' || presetId === 'bhop' || presetId === 'scoutzknivez'
      ? new Set(['Round & match', 'Teams & bots', 'Combat & damage', 'Movement & physics'])
      : new Set(['Round & match', 'Teams & bots', 'Combat & damage', 'Admin playground']);
    return settings.filter(setting => relevantGroups.has(setting.group));
  }

  private normalizeLiveGroup(): void {
    const groups = this.liveGroups();
    if (groups.length && !groups.includes(this.activeLiveGroup())) {
      this.activeLiveGroup.set(groups[0]);
    }
  }

  liveActionGroups(): string[] {
    return [...new Set(this.utilityLiveActions().map(action => action.group))];
  }

  liveActionsFor(group: string): Cs2QuickAction[] {
    return this.utilityLiveActions().filter(action => action.group === group);
  }

  combatActions(): Cs2QuickAction[] {
    return this.liveControl()?.actions.filter(action => action.id.startsWith('combat-')) ?? [];
  }

  policyActions(policy: 'bhop' | 'respawn' | 'hud'): Cs2QuickAction[] {
    const ids = policy === 'bhop' ? ['enable-bhop', 'disable-bhop']
      : policy === 'respawn' ? ['respawn-round', 'respawn-instant']
      : ['hud-hidden', 'hud-timer', 'hud-movement'];
    return this.liveControl()?.actions.filter(action => ids.includes(action.id)) ?? [];
  }

  activeBhopMode(): 'enabled' | 'disabled' | 'mixed' {
    const enabled = this.liveObservedValues()['sv_enablebunnyhopping'] === '1';
    const automatic = this.liveObservedValues()['sv_autobunnyhopping'] === '1';
    return enabled && automatic ? 'enabled' : !enabled && !automatic ? 'disabled' : 'mixed';
  }

  activeRespawnMode(): Cs2RespawnMode | 'mixed' {
    const values = this.liveObservedValues();
    const t = values['mp_respawn_on_death_t'] === '1';
    const ct = values['mp_respawn_on_death_ct'] === '1';
    const endless = values['mp_ignore_round_win_conditions'] === '1';
    return t && ct && endless ? 'instant' : !t && !ct && !endless ? 'round' : 'mixed';
  }

  activeHudMode(): Cs2HudMode {
    return this.liveControl()?.activeHudMode ?? this.activeModeProfile()?.hudMode ?? 'hidden';
  }

  isPolicyActionActive(actionId: string): boolean {
    return actionId === 'enable-bhop' ? this.activeBhopMode() === 'enabled'
      : actionId === 'disable-bhop' ? this.activeBhopMode() === 'disabled'
      : actionId === 'respawn-round' ? this.activeRespawnMode() === 'round'
      : actionId === 'respawn-instant' ? this.activeRespawnMode() === 'instant'
      : actionId === `hud-${this.activeHudMode()}`;
  }

  repairCombatAction(): Cs2QuickAction | null {
    return this.liveControl()?.actions.find(action => action.id === 'repair-team-damage') ?? null;
  }

  selectActionGroup(group: string): void {
    this.activeActionGroup.set(group);
  }

  activeCombatMode(): Cs2CombatMode {
    const values = this.liveObservedValues();
    if (Number(values['mp_damage_scale_ct_body']) === 0 && Number(values['mp_damage_scale_t_body']) === 0) {
      return 'peaceful';
    }
    if (Number(values['mp_teammates_are_enemies']) === 1) {
      return 'ffa';
    }
    return this.livePolicyObserved('combat') ? 'team' : this.activeModeProfile()?.combatMode ?? 'team';
  }

  combatModeForAction(actionId: string): Cs2CombatMode | null {
    return ({
      'combat-peaceful': 'peaceful',
      'combat-team': 'team',
      'combat-ffa': 'ffa'
    } as Record<string, Cs2CombatMode>)[actionId] ?? null;
  }

  private utilityLiveActions(): Cs2QuickAction[] {
    return this.liveControl()?.actions.filter(action =>
      !action.id.startsWith('combat-') &&
      action.id !== 'repair-team-damage' &&
      !['enable-bhop', 'disable-bhop', 'respawn-round', 'respawn-instant', 'hud-hidden', 'hud-timer', 'hud-movement'].includes(action.id)) ?? [];
  }

  updateLiveValue(key: string, event: Event): void {
    this.setLiveValue(key, (event.target as HTMLInputElement | HTMLSelectElement).value);
  }

  setLiveValue(key: string, value: string): void {
    this.liveValues.update(values => ({ ...values, [key]: value }));
    this.liveDirtyKeys.update(keys => value === this.liveObservedValues()[key]
      ? keys.filter(existing => existing !== key)
      : keys.includes(key) ? keys : [...keys, key]);
  }

  isLiveSettingDirty(key: string): boolean {
    return this.liveDirtyKeys().includes(key);
  }

  isLiveSettingObserved(key: string): boolean {
    const control = this.liveControl();
    return !!control?.running && control.liveValueKeys.includes(key);
  }

  liveSettingSource(key: string): 'live' | 'saved' | 'fallback' | 'pending' {
    if (this.isLiveSettingDirty(key)) return 'pending';
    if (this.isLiveSettingObserved(key)) return 'live';
    return this.liveControl()?.running ? 'fallback' : 'saved';
  }

  liveSettingSourceLabel(key: string): string {
    return ({ live: 'LIVE', saved: 'SAVED', fallback: 'FALLBACK', pending: 'NOT APPLIED' } as const)[this.liveSettingSource(key)];
  }

  liveSettingValueLabel(setting: Cs2LiveSetting, value: string | undefined): string {
    if (value === undefined) return 'Unknown';
    if (setting.type === 'boolean') return value === '1' ? 'ON' : 'OFF';
    if (setting.type === 'select') return this.liveOptionLabel(setting, value);
    return value;
  }

  livePolicyObserved(policy: 'combat' | 'bhop' | 'respawn' | 'hud'): boolean {
    if (policy === 'hud') return this.liveControl()?.hudLiveReadSucceeded ?? false;
    const keys = policy === 'combat'
      ? ['mp_friendlyfire', 'mp_teammates_are_enemies', 'mp_damage_scale_ct_head', 'mp_damage_scale_ct_body', 'mp_damage_scale_t_head', 'mp_damage_scale_t_body', 'mp_damage_headshot_only']
      : policy === 'bhop'
        ? ['sv_enablebunnyhopping', 'sv_autobunnyhopping']
        : ['mp_respawn_on_death_t', 'mp_respawn_on_death_ct', 'mp_ignore_round_win_conditions'];
    return keys.every(key => this.isLiveSettingObserved(key));
  }

  applyLiveConfiguration(): void {
    this.error.set('');
    this.liveMessage.set('');
    this.liveSaving.set(true);
    this.api.applyCs2LiveControl(this.id, this.liveValues(), this.liveDirtyKeys()).pipe(finalize(() => this.liveSaving.set(false))).subscribe({
      next: result => {
        this.liveValues.set({ ...result.values });
        this.liveObservedValues.set({ ...result.values });
        this.liveDirtyKeys.set([]);
        this.liveMessage.set(result.message);
        this.appendConsoleMessage(result.message, 'ConfigurationChanged');
        if (result.output) this.appendConsoleMessage(result.output, 'ConsoleOutput');
        this.loadCs2Control(true, true);
      },
      error: error => this.error.set(error.error?.detail ?? 'The live configuration could not be applied.')
    });
  }

  runLiveAction(action: Cs2QuickAction): void {
    this.executeLiveAction(action.id, action.label);
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

  isProfileLive(profile: Cs2ModeProfile | null): boolean {
    const currentMap = this.server()?.currentMap?.trim().toLowerCase();
    return !!profile && !!currentMap && currentMap === profile.mapName.trim().toLowerCase();
  }

  updateNextMapProfile(event: Event): void {
    this.nextMapProfileId.set((event.target as HTMLSelectElement).value);
  }

  updateNextMapDelay(event: Event): void {
    this.nextMapDelaySeconds.set(Number((event.target as HTMLSelectElement).value));
  }

  scheduleMapChange(): void {
    const profileId = this.nextMapProfileId();
    if (!profileId) return;
    this.error.set('');
    this.mapChangeAction.set('schedule');
    this.api.scheduleCs2MapChange(this.id, profileId, this.nextMapDelaySeconds())
      .pipe(finalize(() => this.mapChangeAction.set('')))
      .subscribe({
        next: mapChange => {
          this.liveControl.update(control => control ? { ...control, mapChange } : control);
          this.liveMessage.set(mapChange.message);
        },
        error: error => this.error.set(error.error?.detail ?? 'The map change could not be scheduled.')
      });
  }

  cancelMapChange(): void {
    this.error.set('');
    this.mapChangeAction.set('cancel');
    this.api.cancelCs2MapChange(this.id)
      .pipe(finalize(() => this.mapChangeAction.set('')))
      .subscribe({
        next: mapChange => {
          this.liveControl.update(control => control ? { ...control, mapChange } : control);
          this.liveMessage.set(mapChange.message);
        },
        error: error => this.error.set(error.error?.detail ?? 'The scheduled map change could not be cancelled.')
      });
  }

  mapChangeRemaining(): number {
    this.mapClock();
    const executeAt = this.liveControl()?.mapChange.executeAt;
    return executeAt ? Math.max(0, Math.ceil((new Date(executeAt).getTime() - Date.now()) / 1000)) : 0;
  }

  formatCountdown(seconds: number): string {
    const minutes = Math.floor(seconds / 60);
    const remainder = seconds % 60;
    return minutes > 0 ? `${minutes}:${remainder.toString().padStart(2, '0')}` : `${remainder}s`;
  }

  private refreshMapChangeState(): void {
    this.api.cs2MapChange(this.id).subscribe({
      next: mapChange => {
        this.liveControl.update(control => control ? { ...control, mapChange } : control);
        if (mapChange.status === 'completed') this.refreshCs2ModeState();
      }
    });
  }

  selectPreset(presetId: string): void {
    const preset = this.modeCatalog()?.presets.find(item => item.id === presetId);
    if (!preset) return;
    this.selectedPresetId.set(preset.id);
    this.modeCombat.set(preset.defaultCombatMode);
    this.modeAmmo.set(preset.defaultAmmoMode);
    this.modeHud.set(preset.defaultHudMode);
    this.modeRespawn.set(preset.defaultRespawnMode);
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
    this.modeCombat.set(profile.combatMode);
    this.modeAmmo.set(profile.ammoMode);
    this.modeHud.set(profile.hudMode);
    this.modeRespawn.set(profile.respawnMode);
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

  updateModeCombat(event: Event): void {
    this.modeCombat.set((event.target as HTMLSelectElement).value as Cs2CombatMode);
  }

  updateModeAmmo(event: Event): void {
    this.modeAmmo.set((event.target as HTMLSelectElement).value as Cs2AmmoMode);
  }

  updateModeHud(event: Event): void {
    this.modeHud.set((event.target as HTMLSelectElement).value as Cs2HudMode);
  }

  updateModeRespawn(event: Event): void {
    this.modeRespawn.set((event.target as HTMLSelectElement).value as Cs2RespawnMode);
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
      overrides: this.modeOverrides(),
      combatMode: this.modeCombat(),
      ammoMode: this.modeAmmo(),
      hudMode: this.modeHud(),
      respawnMode: this.modeRespawn()
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
        const isRunning = server.status === 'Running' && server.process.isRunning;
        this.server.set(server);
        this.liveControl.update(control => control ? {
          ...control,
          running: isRunning
        } : control);
        if (this.publicationPort() === null) this.publicationPort.set(server.publication.publicPort);
        if (this.tab() === 'control' && this.liveControl()) {
          this.refreshMapChangeState();
          if (server.status === 'Running' && server.process.isRunning && Date.now() - this.lastLiveRefreshAt >= 10000) {
            this.loadCs2Control(true, true);
          } else if (!isRunning && (this.liveControl()?.liveValueKeys.length ?? 0) > 0) {
            this.loadCs2Control(true, true);
          }
        }
        if (loadModes && server.templateId === 'counter-strike-2') this.loadCs2Modes();
        else if (server.templateId === 'counter-strike-2' && this.modeState() &&
          (this.tab() === 'modes' ||
            (server.status === 'Running' && this.activeModeProfile()?.workshopInstallState === 'pending' && !this.isProfileLive(this.activeModeProfile())))) {
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
        const verifiedPolicy = actionId.startsWith('combat-') ||
          actionId === 'repair-team-damage' ||
          actionId.startsWith('respawn-') ||
          actionId.startsWith('hud-') ||
          actionId === 'enable-bhop' ||
          actionId === 'disable-bhop';
        const message = verifiedPolicy && result.output ? result.output : `${label} executed successfully.`;
        this.liveMessage.set(message);
        this.reflectLiveAction(actionId);
        this.appendConsoleMessage(`> ${label} (${result.transport})`, 'ConsoleCommand');
        if (result.output) this.appendConsoleMessage(result.output, 'ConsoleOutput');
        this.loadCs2Control(true, true);
      },
      error: error => this.error.set(error.error?.detail ?? `The '${label}' action could not be executed.`)
    });
  }

  private reflectLiveAction(actionId: string): void {
    const combatMode = ({
      'combat-peaceful': 'peaceful',
      'combat-team': 'team',
      'combat-ffa': 'ffa'
    } as const)[actionId as 'combat-peaceful' | 'combat-team' | 'combat-ffa'];
    if (combatMode) {
      const damageScale = combatMode === 'peaceful' ? '0' : '1';
      this.reflectObservedValues({
        mp_friendlyfire: combatMode === 'ffa' ? '1' : '0',
        mp_teammates_are_enemies: combatMode === 'ffa' ? '1' : '0',
        mp_damage_scale_ct_head: damageScale,
        mp_damage_scale_ct_body: damageScale,
        mp_damage_scale_t_head: damageScale,
        mp_damage_scale_t_body: damageScale,
        mp_damage_headshot_only: '0'
      });
      this.updateActiveProfilePolicy({ combatMode });
      return;
    }

    if (actionId === 'enable-bhop' || actionId === 'disable-bhop') {
      const value = actionId === 'enable-bhop' ? '1' : '0';
      this.reflectObservedValues({
        sv_enablebunnyhopping: value,
        sv_autobunnyhopping: value
      });
      return;
    }

    if (actionId === 'respawn-round' || actionId === 'respawn-instant') {
      const respawnMode: Cs2RespawnMode = actionId === 'respawn-instant' ? 'instant' : 'round';
      const value = respawnMode === 'instant' ? '1' : '0';
      this.reflectObservedValues({
        mp_respawn_on_death_t: value,
        mp_respawn_on_death_ct: value,
        mp_ignore_round_win_conditions: value
      });
      this.updateActiveProfilePolicy({ respawnMode });
      return;
    }

    if (actionId.startsWith('hud-')) {
      const hudMode = actionId.slice(4) as Cs2HudMode;
      this.liveControl.update(control => control ? { ...control, activeHudMode: hudMode } : control);
      this.updateActiveProfilePolicy({ hudMode });
    }
  }

  private reflectObservedValues(update: Record<string, string>): void {
    const keys = new Set(Object.keys(update));
    this.liveObservedValues.update(values => ({ ...values, ...update }));
    this.liveValues.update(values => ({ ...values, ...update }));
    this.liveDirtyKeys.update(dirty => dirty.filter(key => !keys.has(key)));
  }

  private updateActiveProfilePolicy(update: Partial<Pick<Cs2ModeProfile, 'combatMode' | 'respawnMode' | 'hudMode'>>): void {
    this.modeState.update(state => state ? {
      ...state,
      profiles: state.profiles.map(profile => profile.id === state.activeProfileId
        ? { ...profile, ...update }
        : profile)
    } : state);
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
