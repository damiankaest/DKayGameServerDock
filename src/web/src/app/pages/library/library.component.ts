import { Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { GameTemplate } from '../../core/models';

@Component({
  selector: 'app-library',
  templateUrl: './library.component.html'
})
export class LibraryComponent {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  readonly templates = signal<GameTemplate[]>([]);
  readonly search = signal('');

  constructor() {
    this.api.templates().subscribe(templates => this.templates.set(templates));
  }

  filtered(): GameTemplate[] {
    const query = this.search().trim().toLowerCase();
    return query ? this.templates().filter(template => `${template.name} ${template.description}`.toLowerCase().includes(query)) : this.templates();
  }

  create(template: GameTemplate): void {
    void this.router.navigate(['/servers'], { queryParams: { create: template.id } });
  }

  updateSearch(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value);
  }
}

