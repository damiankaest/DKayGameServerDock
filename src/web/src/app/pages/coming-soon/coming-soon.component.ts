import { Component, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';

@Component({
  selector: 'app-coming-soon',
  template: `<div class="page narrow-page"><header class="page-header"><div><p class="eyebrow">ROADMAP</p><h1>{{ title }}</h1><p class="muted">{{ text }}</p></div></header><div class="empty-state"><span class="game-icon large">◇</span><h3>Foundation ready</h3><p>This area is intentionally visible but not presented as finished functionality.</p></div></div>`
})
export class ComingSoonComponent {
  private readonly data = inject(ActivatedRoute).snapshot.data;
  readonly title = this.data['title'] as string;
  readonly text = this.data['text'] as string;
}

