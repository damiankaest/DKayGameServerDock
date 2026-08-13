import { Component, inject, OnDestroy, signal } from '@angular/core';
import { ApiService } from '../../core/api.service';
import { PublicServerList } from '../../core/models';

@Component({
  selector: 'app-public-servers',
  templateUrl: './public-servers.component.html'
})
export class PublicServersComponent implements OnDestroy {
  private readonly api = inject(ApiService);
  private readonly refreshTimer: number;
  readonly listing = signal<PublicServerList | null>(null);
  readonly loading = signal(true);
  readonly refreshing = signal(false);
  readonly error = signal('');
  readonly copied = signal('');
  readonly copyError = signal('');

  constructor() {
    this.load();
    this.refreshTimer = window.setInterval(() => this.load(true), 20_000);
  }

  ngOnDestroy(): void {
    window.clearInterval(this.refreshTimer);
  }

  load(silent = false): void {
    if (silent && this.listing()) this.refreshing.set(true);
    else this.loading.set(true);
    this.error.set('');
    this.api.publicServers().subscribe({
      next: listing => {
        this.listing.set(listing);
        this.loading.set(false);
        this.refreshing.set(false);
      },
      error: () => {
        this.error.set(this.listing()
          ? 'The list could not be refreshed. The last known server status is still shown.'
          : 'The public server list is currently unavailable.');
        this.loading.set(false);
        this.refreshing.set(false);
      }
    });
  }

  copy(address: string): void {
    this.copyError.set('');
    void this.copyText(address)
      .then(() => {
        this.copied.set(address);
        setTimeout(() => this.copied.set(''), 1800);
      })
      .catch(() => this.copyError.set('Copy failed. Select the address and copy it manually.'));
  }

  onlineCount(listing: PublicServerList): number {
    return listing.servers.filter(server => server.status === 'Running').length;
  }

  formatUpdated(value: string): string {
    const date = new Date(value);
    return Number.isNaN(date.getTime())
      ? value
      : date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' });
  }

  private async copyText(value: string): Promise<void> {
    if (navigator.clipboard?.writeText) {
      try {
        await navigator.clipboard.writeText(value);
        return;
      } catch {
        // HTTP guest portals may not expose the secure Clipboard API.
      }
    }

    const input = document.createElement('textarea');
    input.value = value;
    input.style.position = 'fixed';
    input.style.opacity = '0';
    document.body.appendChild(input);
    input.select();
    const copied = document.execCommand('copy');
    input.remove();
    if (!copied) throw new Error('The browser refused the copy command.');
  }
}
