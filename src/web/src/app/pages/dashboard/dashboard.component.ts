import { Component, inject, OnDestroy, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { GameServer, HostSnapshot } from '../../core/models';

@Component({
  selector: 'app-dashboard',
  imports: [RouterLink],
  templateUrl: './dashboard.component.html'
})
export class DashboardComponent implements OnDestroy {
  private readonly api = inject(ApiService);
  private readonly refreshTimer: ReturnType<typeof setInterval>;
  readonly host = signal<HostSnapshot | null>(null);
  readonly servers = signal<GameServer[]>([]);
  readonly loadingAction = signal('');
  readonly error = signal('');

  constructor() {
    this.refresh();
    this.refreshTimer = setInterval(() => this.refresh(), 5000);
  }

  ngOnDestroy(): void {
    clearInterval(this.refreshTimer);
  }

  refresh(): void {
    forkJoin({ host: this.api.host(), servers: this.api.servers() }).subscribe({
      next: result => {
        this.host.set(result.host);
        this.servers.set(result.servers);
        this.error.set('');
      },
      error: () => this.error.set('Live host data could not be loaded.')
    });
  }

  action(server: GameServer, action: 'start' | 'stop' | 'restart'): void {
    this.loadingAction.set(`${server.id}:${action}`);
    this.api.serverAction(server.id, action).subscribe({
      next: () => {
        this.loadingAction.set('');
        this.refresh();
      },
      error: error => {
        this.loadingAction.set('');
        this.error.set(error.error?.detail ?? 'The server action failed.');
      }
    });
  }

  memoryUsedPercent(host: HostSnapshot): number {
    return host.totalMemoryBytes ? (host.totalMemoryBytes - host.availableMemoryBytes) / host.totalMemoryBytes * 100 : 0;
  }

  diskUsedPercent(host: HostSnapshot): number {
    const disk = host.disks[0];
    return disk?.totalBytes ? (disk.totalBytes - disk.availableBytes) / disk.totalBytes * 100 : 0;
  }

  formatBytes(bytes: number): string {
    if (!bytes) return '0 GB';
    return `${(bytes / 1024 / 1024 / 1024).toFixed(1)} GB`;
  }
}

