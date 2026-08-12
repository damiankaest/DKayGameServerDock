import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { finalize, forkJoin } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { GameServer, GameTemplate } from '../../core/models';

@Component({
  selector: 'app-servers',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './servers.component.html'
})
export class ServersComponent {
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly formBuilder = inject(FormBuilder);
  readonly servers = signal<GameServer[]>([]);
  readonly templates = signal<GameTemplate[]>([]);
  readonly selectedTemplate = signal<GameTemplate | null>(null);
  readonly settings = signal<Record<string, string>>({});
  readonly showCreate = signal(false);
  readonly saving = signal(false);
  readonly actioning = signal('');
  readonly error = signal('');
  readonly form = this.formBuilder.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(120)]],
    templateId: ['', Validators.required],
    version: ['latest', Validators.required],
    port: [25565, [Validators.required, Validators.min(1), Validators.max(65535)]],
    ramLimitMb: [4096, [Validators.required, Validators.min(512)]]
  });

  constructor() {
    forkJoin({ servers: this.api.servers(), templates: this.api.templates() }).subscribe({
      next: result => {
        this.servers.set(result.servers);
        this.templates.set(result.templates);
        const requested = this.route.snapshot.queryParamMap.get('create');
        if (requested) {
          const template = result.templates.find(item => item.id === requested) ?? result.templates[0];
          if (template) this.openCreate(template);
        }
      },
      error: () => this.error.set('Servers could not be loaded.')
    });
  }

  openCreate(template?: GameTemplate): void {
    const selected = template ?? this.templates()[0];
    if (!selected) return;
    this.showCreate.set(true);
    this.selectTemplate(selected.id);
  }

  closeCreate(): void {
    if (!this.saving()) this.showCreate.set(false);
  }

  selectTemplate(templateId: string): void {
    const template = this.templates().find(item => item.id === templateId) ?? null;
    this.selectedTemplate.set(template);
    if (!template) return;
    this.form.patchValue({
      templateId: template.id,
      name: template.name.includes('Minecraft') ? 'Friends Survival' : 'Competitive Server',
      port: template.defaultPort,
      ramLimitMb: template.defaultRamMb,
      version: 'latest'
    });
    this.settings.set(Object.fromEntries(template.settings.map(setting => [setting.key, setting.defaultValue ?? ''])));
  }

  setSetting(key: string, event: Event): void {
    const value = (event.target as HTMLInputElement | HTMLSelectElement).value;
    this.settings.update(settings => ({ ...settings, [key]: value }));
  }

  setBoolean(key: string, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    this.settings.update(settings => ({ ...settings, [key]: String(checked) }));
  }

  create(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.error.set('');
    const value = this.form.getRawValue();
    this.api.createServer({
      ...value,
      queryPort: null,
      rconPort: null,
      settings: this.settings()
    }).pipe(finalize(() => this.saving.set(false))).subscribe({
      next: server => {
        this.servers.update(items => [...items, server]);
        this.showCreate.set(false);
      },
      error: error => this.error.set(error.error?.detail ?? 'The server could not be created.')
    });
  }

  action(server: GameServer, action: 'start' | 'stop' | 'restart'): void {
    this.error.set('');
    this.actioning.set(`${server.id}:${action}`);
    this.api.serverAction(server.id, action).pipe(finalize(() => this.actioning.set(''))).subscribe({
      next: () => this.reload(),
      error: error => this.error.set(error.error?.detail ?? 'The action failed.')
    });
  }

  delete(server: GameServer): void {
    if (!window.confirm(`Delete "${server.name}" and all files in its managed server directory? This cannot be undone.`)) {
      return;
    }

    this.error.set('');
    this.actioning.set(`${server.id}:delete`);
    this.api.deleteServer(server.id).pipe(finalize(() => this.actioning.set(''))).subscribe({
      next: () => this.servers.update(items => items.filter(item => item.id !== server.id)),
      error: error => this.error.set(error.error?.detail ?? 'The server could not be deleted.')
    });
  }

  private reload(): void {
    this.api.servers().subscribe(servers => this.servers.set(servers));
  }
}
