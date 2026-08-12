import { Component, inject, signal } from '@angular/core';
import { forkJoin } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { HostReadinessSnapshot, HostSnapshot } from '../../core/models';

@Component({ selector: 'app-host', templateUrl: './host.component.html' })
export class HostComponent {
  private readonly api = inject(ApiService);
  readonly host = signal<HostSnapshot | null>(null);
  readonly readiness = signal<HostReadinessSnapshot | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  constructor() {
    this.refresh();
  }

  refresh(): void {
    this.loading.set(true);
    this.error.set(null);
    forkJoin({ host: this.api.host(), readiness: this.api.hostReadiness() }).subscribe({
      next: result => {
        this.host.set(result.host);
        this.readiness.set(result.readiness);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Host diagnostics could not be loaded. Check the Dock service logs.');
        this.loading.set(false);
      }
    });
  }

  gb(bytes: number): string { return `${(bytes / 1024 / 1024 / 1024).toFixed(1)} GB`; }
}
