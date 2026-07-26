import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import {
  AlertComponent,
  ButtonCloseDirective,
  ButtonDirective,
  CardBodyComponent,
  CardComponent,
  ColComponent,
  ContainerComponent,
  FormControlDirective,
  FormLabelDirective,
  ModalBodyComponent,
  ModalComponent,
  ModalFooterComponent,
  ModalHeaderComponent,
  ModalTitleDirective,
  RowComponent,
  SpinnerComponent,
  TableDirective
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { User, UserService, CreateUserRequest, UpdateUserRequest } from '../../core/services/user.service';
import { AuthService } from '../../core/services/auth.service';

type PageMode = 'list' | 'add' | 'edit';

@Component({
  selector: 'app-user-management',
  templateUrl: './user-management.component.html',
  imports: [
    ContainerComponent, RowComponent, ColComponent,
    CardComponent, CardBodyComponent,
    ReactiveFormsModule, FormControlDirective, FormLabelDirective,
    ButtonDirective, ButtonCloseDirective, SpinnerComponent, AlertComponent, IconDirective,
    TableDirective, DatePipe,
    ModalComponent, ModalHeaderComponent, ModalTitleDirective, ModalBodyComponent, ModalFooterComponent
  ]
})
export class UserManagementComponent implements OnInit {
  private userService = inject(UserService);
  private authService = inject(AuthService);
  private router       = inject(Router);
  readonly #destroyRef = inject(DestroyRef);

  mode = signal<PageMode>('list');

  users       = this.userService.users;
  loadingList = signal(false);
  listError   = signal<string | null>(null);

  saving     = signal(false);
  saveStatus = signal<'idle' | 'success' | 'error'>('idle');
  saveError  = signal('');

  editingUser           = signal<User | null>(null);
  confirmDeleteVisible  = signal(false);
  deleteTarget          = signal<User | null>(null);
  deleting              = signal(false);
  deleteError           = signal<string | null>(null);

  form = new FormGroup({
    login:    new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    password: new FormControl('', { nonNullable: true, validators: [Validators.required] })
  });

  ngOnInit() {
    this.loadUsers();
  }

  private loadUsers() {
    this.loadingList.set(true);
    this.listError.set(null);
    this.userService.loadUsers()
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next:  () =>  this.loadingList.set(false),
        error: err => { this.listError.set(err?.error?.message ?? 'Failed to load users.'); this.loadingList.set(false); }
      });
  }

  showAddForm() {
    this.editingUser.set(null);
    this.form.reset();
    this.form.controls.password.addValidators(Validators.required);
    this.form.controls.password.updateValueAndValidity();
    this.saveStatus.set('idle');
    this.mode.set('add');
  }

  showEditForm(user: User) {
    this.editingUser.set(user);
    this.form.setValue({ login: user.login, password: '' });
    this.form.controls.password.clearValidators();
    this.form.controls.password.updateValueAndValidity();
    this.saveStatus.set('idle');
    this.mode.set('edit');
  }

  cancelAdd() {
    this.editingUser.set(null);
    this.mode.set('list');
  }

  requestDelete(user: User) {
    this.deleteTarget.set(user);
    this.deleteError.set(null);
    this.confirmDeleteVisible.set(true);
  }

  confirmDelete() {
    const user = this.deleteTarget();
    if (!user) return;

    this.deleting.set(true);
    this.deleteError.set(null);

    this.userService.delete(user.id)
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next: () => {
          this.userService.loadUsers().pipe(takeUntilDestroyed(this.#destroyRef)).subscribe();
          this.deleting.set(false);
          this.confirmDeleteVisible.set(false);
          this.deleteTarget.set(null);
        },
        error: (err) => {
          this.deleteError.set(err?.error?.message ?? 'Failed to delete user.');
          this.deleting.set(false);
        }
      });
  }

  submit() {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    // Capturé avant l'appel réseau : dès que ce premier utilisateur est créé, le backend active
    // l'authentification globale (voir Inkhound.Web/CLAUDE.md, mode bootstrap ouvert) — la session
    // virtuelle en cours cesse d'être valide et l'utilisateur doit se connecter avec ses nouveaux
    // identifiants plutôt que de revenir simplement à la liste.
    const wasOpenMode = this.mode() === 'add' && this.users().length === 0;

    this.saving.set(true);
    this.saveStatus.set('idle');

    if (this.mode() === 'edit') {
      const user = this.editingUser()!;
      const request: UpdateUserRequest = {
        login:    this.form.controls.login.value,
        password: this.form.controls.password.value || null
      };
      this.userService.update(user.id, request)
        .pipe(takeUntilDestroyed(this.#destroyRef))
        .subscribe({
          next: () => {
            this.userService.loadUsers().pipe(takeUntilDestroyed(this.#destroyRef)).subscribe();
            this.saving.set(false);
            this.editingUser.set(null);
            this.mode.set('list');
          },
          error: (err) => {
            this.saving.set(false);
            this.saveStatus.set('error');
            this.saveError.set(err?.error?.message ?? 'Failed to update user.');
          }
        });
    } else {
      const request: CreateUserRequest = {
        login:    this.form.controls.login.value,
        password: this.form.controls.password.value
      };
      this.userService.create(request)
        .pipe(takeUntilDestroyed(this.#destroyRef))
        .subscribe({
          next: () => {
            this.saving.set(false);
            if (wasOpenMode) {
              this.authService.logout();
              this.router.navigateByUrl('/login');
            } else {
              this.userService.loadUsers().pipe(takeUntilDestroyed(this.#destroyRef)).subscribe();
              this.mode.set('list');
            }
          },
          error: (err) => {
            this.saving.set(false);
            this.saveStatus.set('error');
            this.saveError.set(err?.error?.message ?? 'Failed to create user.');
          }
        });
    }
  }
}
