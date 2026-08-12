import { DatePipe } from '@angular/common';
import { Component, inject, OnDestroy, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { GameServer, ServerEvent } from '../../core/models';
import { RealtimeService } from '../../core/realtime.service';

@Component({
  selector: 'app-server-detail',
  imports: [RouterLink, DatePipe],
  templateUrl: './server-detail.component.html'
})
export class ServerDetailComponent implements OnDestroy {
  private readonly api = inject(ApiService);
  private readonly realtime = inject(RealtimeService);
  private readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id')!;
  private readonly refreshTimer: ReturnType<typeof setInterval>;
  readonly server = signal<GameServer | null>(null);
  readonly logs = signal<ServerEvent[]>([]);
  readonly tab = signal<'overview' | 'console' | 'players'>('overview');
  readonly command = signal('');
  readonly progress = signal<{ percent: number; stage: string; message: string } | null>(null);
  readonly error = signal('');
  readonly publicationPort = signal<number | null>(null);
  readonly publicationSaving = signal(false);
  readonly copied = signal('');

  constructor() {
    this.load();
    void this.realtime.connect(this.id, {
      consoleLine: line => this.logs.update(logs => [...logs, { id: 0, serverId: this.id, type: 'ConsoleOutput', ...line }].slice(-500)),
      statusChanged: status => {
        this.server.update(server => server ? { ...server, status } : server);
        this.loadServer();
      },
      installationProgress: progress => this.progress.set(progress)
    }).catch(() => this.error.set('Live updates are temporarily unavailable.'));
    this.refreshTimer = setInterval(() => this.loadServer(), 5000);
  }

  ngOnDestroy(): void {
    clearInterval(this.refreshTimer);
    void this.realtime.disconnect();
  }

  load(): void {
    forkJoin({ server: this.api.server(this.id), logs: this.api.logs(this.id) }).subscribe({
      next: result => {
        this.server.set(result.server);
        this.publicationPort.set(result.server.publication.publicPort);
        this.logs.set([...result.logs].reverse());
      },
      error: error => this.error.set(error.error?.detail ?? 'The server could not be loaded.')
    });
  }

  loadServer(): void {
    this.api.server(this.id).subscribe(server => {
      this.server.set(server);
      if (this.publicationPort() === null) this.publicationPort.set(server.publication.publicPort);
    });
  }

  action(action: 'start' | 'stop' | 'restart' | 'kill' | 'update'): void {
    this.error.set('');
    this.api.serverAction(this.id, action).subscribe({
      next: () => this.loadServer(),
      error: error => this.error.set(error.error?.detail ?? 'The server action failed.')
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
    return this.logs().filter(log => log.type === 'ConsoleOutput');
  }

  formatBytes(bytes: number): string {
    return bytes ? `${(bytes / 1024 / 1024).toFixed(0)} MB` : '0 MB';
  }
}
