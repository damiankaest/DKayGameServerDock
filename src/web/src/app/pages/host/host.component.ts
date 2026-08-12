import { Component, inject, signal } from '@angular/core';
import { ApiService } from '../../core/api.service';
import { HostSnapshot } from '../../core/models';

@Component({ selector: 'app-host', templateUrl: './host.component.html' })
export class HostComponent {
  readonly host = signal<HostSnapshot | null>(null);

  constructor() {
    inject(ApiService).host().subscribe(host => this.host.set(host));
  }

  gb(bytes: number): string { return `${(bytes / 1024 / 1024 / 1024).toFixed(1)} GB`; }
}

