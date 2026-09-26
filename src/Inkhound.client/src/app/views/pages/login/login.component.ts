import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { IconDirective } from '@coreui/icons-angular';
import {
  AlertComponent,
  ButtonDirective,
  CardBodyComponent,
  CardComponent,
  FormControlDirective,
  FormDirective,
  InputGroupComponent,
  InputGroupTextDirective,
  SpinnerComponent
} from '@coreui/angular';
import { AuthService } from '../../../core/services/auth.service';

// Écran de connexion aux couleurs InkHound : fond illustré plein écran + carte vitrée centrée
// (styles dans login.component.scss). Logique d'authentification inchangée (AuthService).
@Component({
  selector: 'app-login',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss',
  imports: [
    FormsModule,
    CardComponent, CardBodyComponent, FormDirective, InputGroupComponent, InputGroupTextDirective,
    IconDirective, FormControlDirective, ButtonDirective, AlertComponent, SpinnerComponent
  ]
})
export class LoginComponent {
  private auth = inject(AuthService);
  private router = inject(Router);

  login = signal('');
  password = signal('');
  error = signal('');
  loading = signal(false);

  submit() {
    this.error.set('');
    this.loading.set(true);
    this.auth.login(this.login(), this.password()).subscribe({
      next: () => this.router.navigateByUrl('/dashboard'),
      error: () => {
        this.error.set('Invalid credentials.');
        this.loading.set(false);
      }
    });
  }
}
