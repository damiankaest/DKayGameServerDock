import { Component, inject, signal } from '@angular/core';
import { ApiService } from '../../core/api.service';
import { PublicServerList } from '../../core/models';

@Component({
  selector: 'app-public-servers',
  templateUrl: './public-servers.component.html'
})
export class PublicServersComponent {
  private readonly api = inject(ApiService);
  readonly listing = signal<PublicServerList | null>(null);
  readonly loading = signal(true);
  readonly error = signal('');
  readonly copied = signal('');

  constructor() {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set('');
    this.api.publicServers().subscribe({
      next: listing => {
        this.listing.set(listing);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('The public server list is currently unavailable.');
        this.loading.set(false);
      }
    });
  }

  copy(address: string): void {
    void this.copyText(address).then(() => {
      this.copied.set(address);
      setTimeout(() => this.copied.set(''), 1800);
    });
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
    document.execCommand('copy');
    input.remove();
  }
}
