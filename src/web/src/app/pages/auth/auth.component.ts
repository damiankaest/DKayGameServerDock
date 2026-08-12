import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { finalize } from 'rxjs';
import { ApiService } from '../../core/api.service';

@Component({
  selector: 'app-auth',
  imports: [ReactiveFormsModule],
  templateUrl: './auth.component.html'
})
export class AuthComponent {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly formBuilder = inject(FormBuilder);

  readonly setupRequired = signal(false);
  readonly loading = signal(true);
  readonly error = signal('');
  readonly form = this.formBuilder.nonNullable.group({
    userName: ['admin', [Validators.required]],
    password: ['', [Validators.required, Validators.minLength(10)]]
  });

  constructor() {
    this.api.authStatus().subscribe({
      next: status => {
        this.setupRequired.set(status.setupRequired);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('The backend is not reachable. Start the Dock service and try again.');
        this.loading.set(false);
      }
    });
  }

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.loading.set(true);
    this.error.set('');
    const value = this.form.getRawValue();
    this.api.authenticate(this.setupRequired(), value.userName, value.password)
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: () => void this.router.navigate(['/']),
        error: () => this.error.set(this.setupRequired()
          ? 'The administrator could not be created.'
          : 'User name or password is incorrect.')
      });
  }
}

