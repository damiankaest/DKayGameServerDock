export interface TemplateSetting {
  key: string;
  label: string;
  type: 'text' | 'number' | 'password' | 'boolean' | 'select';
  required: boolean;
  defaultValue: string | null;
  options: string[] | null;
  secret: boolean;
}

export interface GameTemplate {
  id: string;
  name: string;
  description: string;
  category: string;
  icon: string;
  installer: string;
  defaultPort: number;
  defaultRamMb: number;
  networkProtocols: string[];
  capabilities: string;
  settings: TemplateSetting[];
}

export interface ProcessSnapshot {
  isRunning: boolean;
  processId: number | null;
  exitCode: number | null;
  startedAt: string | null;
  uptime: string | null;
  cpuPercent: number;
  memoryBytes: number;
}

export interface PlayerInfo {
  name: string;
  id: string;
  ping: number | null;
  connectionTime: string | null;
}

export interface GameServer {
  id: string;
  name: string;
  templateId: string;
  templateName: string;
  templateIcon: string;
  version: string;
  port: number;
  queryPort: number | null;
  rconPort: number | null;
  ramLimitMb: number;
  autostart: boolean;
  autoRestart: boolean;
  status: string;
  processId: number | null;
  exitCode: number | null;
  lastError: string | null;
  createdAt: string;
  updatedAt: string;
  startedAt: string | null;
  settings: Record<string, string>;
  process: ProcessSnapshot;
  players: PlayerInfo[];
  currentMap: string | null;
  capabilities: string;
  networkProtocols: string[];
  publication: ServerPublication;
}

export interface ServerPublication {
  published: boolean;
  publicPort: number;
  portalEnabled: boolean;
  address: string | null;
  portalUrl: string | null;
}

export interface PublicServer {
  name: string;
  templateName: string;
  templateIcon: string;
  status: string;
  joinAddress: string;
  publicPort: number;
  protocols: string[];
  passwordProtected: boolean;
  maxPlayers: number | null;
  playerCount: number;
  botCount: number;
  players: PublicPlayer[];
  mode: string | null;
  map: string | null;
  maps: PublicMapStats[];
  recordsAvailable: boolean;
  recordsMessage: string;
  updatedAt: string;
}

export interface PublicMapRecord {
  rank: number;
  playerName: string;
  timerTicks: number;
  formattedTime: string;
  completions: number;
  achievedAt: string | null;
}

export interface PublicMapStats {
  profileId: string;
  mapName: string;
  title: string;
  workshopId: string | null;
  previewUrl: string | null;
  presetName: string;
  workshopInstallState: 'local' | 'pending' | 'installed';
  active: boolean;
  playCount: number;
  lastPlayedAt: string | null;
  uniqueRunners: number;
  totalCompletions: number;
  records: PublicMapRecord[];
}

export interface PublicPlayer {
  name: string;
  ping: number | null;
  connectedFor: string | null;
}

export interface PublicServerList {
  name: string;
  servers: PublicServer[];
  generatedAt: string;
}

export interface DiskSnapshot {
  name: string;
  rootPath: string;
  totalBytes: number;
  availableBytes: number;
}

export interface HostSnapshot {
  hostName: string;
  operatingSystem: string;
  architecture: string;
  lanAddresses: string[];
  uptime: string;
  cpuPercent: number;
  totalMemoryBytes: number;
  availableMemoryBytes: number;
  disks: DiskSnapshot[];
}

export interface DirectoryReadiness {
  path: string;
  exists: boolean;
  writable: boolean;
  message: string;
}

export interface RuntimeReadiness {
  id: string;
  name: string;
  purpose: string;
  configuredValue: string;
  resolvedPath: string | null;
  available: boolean;
  version: string | null;
  message: string;
}

export interface HostReadinessSnapshot {
  ready: boolean;
  dataRoot: DirectoryReadiness;
  serversRoot: DirectoryReadiness;
  runtimes: RuntimeReadiness[];
  checkedAt: string;
}

export interface ServerEvent {
  id: number;
  serverId: string;
  type: string;
  message: string;
  dataJson: string | null;
  occurredAt: string;
}

export interface ConsoleCommandResult {
  transport: string;
  output: string | null;
}

export interface ServerSelfTestResult {
  passed: boolean;
  transport: string;
  port: number;
  processId: number | null;
  message: string;
  output: string | null;
  checkedAt: string;
}

export interface CreateServerRequest {
  name: string;
  templateId: string;
  version: string;
  port: number;
  queryPort: number | null;
  rconPort: number | null;
  ramLimitMb: number;
  settings: Record<string, string>;
}

export interface Cs2ConVar {
  key: string;
  label: string;
  type: 'integer' | 'decimal' | 'boolean' | 'text';
  defaultValue: string;
  editable: boolean;
  description: string;
  minimum: number | null;
  maximum: number | null;
  options: string[] | null;
}

export interface Cs2ModePreset {
  id: string;
  name: string;
  category: string;
  icon: string;
  description: string;
  mapPrefixes: string[];
  recommendedPackageIds: string[];
  settings: Cs2ConVar[];
  defaultCombatMode: Cs2CombatMode;
  defaultAmmoMode: Cs2AmmoMode;
  defaultHudMode: Cs2HudMode;
  defaultRespawnMode: Cs2RespawnMode;
  defaultPracticeMode: Cs2PracticeMode;
}

export type Cs2CombatMode = 'peaceful' | 'team' | 'ffa';
export type Cs2AmmoMode = 'standard' | 'infinite-magazine' | 'infinite-reserve';
export type Cs2HudMode = 'hidden' | 'timer' | 'movement';
export type Cs2RespawnMode = 'round' | 'instant';
export type Cs2PracticeMode = 'disabled' | 'ground' | 'anywhere';

export interface Cs2ManagedPackage {
  id: string;
  name: string;
  kind: string;
  description: string;
  publisher: string;
  projectUrl: string;
  automaticInstall: boolean;
  experimental: boolean;
  dependencyIds: string[];
}

export interface Cs2ModeCatalog {
  presets: Cs2ModePreset[];
  packages: Cs2ManagedPackage[];
}

export interface Cs2ModeProfile {
  id: string;
  presetId: string;
  presetName: string;
  mapName: string;
  workshopId: string | null;
  workshopTitle: string | null;
  workshopPreviewUrl: string | null;
  workshopInstallState: 'local' | 'pending' | 'installed';
  botQuota: number;
  botDifficulty: number;
  combatMode: Cs2CombatMode;
  ammoMode: Cs2AmmoMode;
  hudMode: Cs2HudMode;
  respawnMode: Cs2RespawnMode;
  practiceMode: Cs2PracticeMode;
  overrides: Record<string, string>;
  recommendedPackageIds: string[];
  updatedAt: string;
}

export interface Cs2ManagedPackageState extends Cs2ManagedPackage {
  installed: boolean;
  installedVersion: string | null;
  installedAt: string | null;
}

export interface Cs2ModeState {
  activeProfileId: string | null;
  profiles: Cs2ModeProfile[];
  packages: Cs2ManagedPackageState[];
  workshop: Cs2WorkshopAccessState;
}

export interface Cs2WorkshopAccessState {
  configured: boolean;
  maskedKey: string | null;
  protectedFromGameUpdates: boolean;
  message: string;
}

export interface Cs2WorkshopMap {
  publishedFileId: string;
  title: string;
  mapName: string;
  previewUrl: string | null;
  workshopUrl: string;
  fileSize: number;
  subscriptions: number;
  updatedAt: string | null;
  tags: string[];
}

export interface Cs2WorkshopSearchResult {
  query: string;
  total: number;
  items: Cs2WorkshopMap[];
}

export interface ConfigureCs2WorkshopKeyResult {
  state: Cs2WorkshopAccessState;
  message: string;
}

export interface ApplyCs2ModePresetRequest {
  presetId: string;
  mapName: string;
  workshopId: string | null;
  botQuota: number;
  botDifficulty: number;
  installRecommendedPackages: boolean;
  overrides: Record<string, string>;
  combatMode: Cs2CombatMode;
  ammoMode: Cs2AmmoMode;
  hudMode: Cs2HudMode;
  respawnMode: Cs2RespawnMode;
  practiceMode: Cs2PracticeMode;
}

export interface Cs2ModeApplyResult {
  state: Cs2ModeState;
  queuedPackageIds: string[];
}

export interface Cs2LiveSetting {
  key: string;
  label: string;
  group: string;
  type: 'integer' | 'decimal' | 'boolean' | 'select';
  defaultValue: string;
  description: string;
  minimum: number | null;
  maximum: number | null;
  step: number | null;
  options: string[] | null;
}

export interface Cs2QuickAction {
  id: string;
  label: string;
  description: string;
  group: string;
  icon: string;
  tone: 'default' | 'primary' | 'danger';
  requiresPlugin: boolean;
}

export interface Cs2GsltState {
  configured: boolean;
  maskedToken: string | null;
  protectedFromGameUpdates: boolean;
  message: string;
}

export interface Cs2LiveControlState {
  running: boolean;
  liveReadSucceeded: boolean;
  liveReadMessage: string;
  observedAt: string;
  liveValueKeys: string[];
  settings: Cs2LiveSetting[];
  values: Record<string, string>;
  actions: Cs2QuickAction[];
  gslt: Cs2GsltState;
  mapChange: Cs2MapChangeState;
  activeHudMode: Cs2HudMode;
  hudLiveReadSucceeded: boolean;
  activePracticeMode: Cs2PracticeMode;
  practiceLiveReadSucceeded: boolean;
  sharpTimerInstalled: boolean;
  activeCombatMode: Cs2CombatMode;
  combatLiveReadSucceeded: boolean;
  combatOverrideActive: boolean;
}

export interface Cs2MapChangeState {
  status: 'idle' | 'scheduled' | 'changing' | 'completed' | 'failed';
  profileId: string | null;
  mapName: string | null;
  workshopId: string | null;
  executeAt: string | null;
  remainingSeconds: number;
  message: string;
}

export interface Cs2LiveConfigurationApplyResult {
  values: Record<string, string>;
  appliedLive: boolean;
  message: string;
  output: string | null;
}

export interface ConfigureCs2GsltResult {
  state: Cs2GsltState;
  appliedLive: boolean;
  message: string;
  output: string | null;
}
