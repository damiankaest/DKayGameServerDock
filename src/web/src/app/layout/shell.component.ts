import { Component, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { ApiService } from '../core/api.service';

@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './shell.component.html'
})
export class ShellComponent {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  readonly menuOpen = signal(false);

  logout(): void {
    this.api.logout().subscribe(() => void this.router.navigate(['/auth']));
  }
}

