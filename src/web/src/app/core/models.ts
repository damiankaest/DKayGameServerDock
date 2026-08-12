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

export interface ServerEvent {
  id: number;
  serverId: string;
  type: string;
  message: string;
  dataJson: string | null;
  occurredAt: string;
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

