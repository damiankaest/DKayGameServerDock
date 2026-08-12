import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { ApiService } from '../../core/api.service';
import { ServerEvent } from '../../core/models';

@Component({
  selector: 'app-activity',
  imports: [DatePipe],
  templateUrl: './activity.component.html'
})
export class ActivityComponent {
  readonly events = signal<ServerEvent[]>([]);

  constructor() {
    inject(ApiService).activity().subscribe(events => this.events.set(events));
  }
}

