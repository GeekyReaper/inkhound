import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs/operators';
import {
  AlertComponent, ButtonDirective,
  CardBodyComponent, CardComponent,
  ColComponent, ContainerComponent, RowComponent,
  FormControlDirective, FormDirective, FormLabelDirective, FormSelectDirective, InputGroupComponent,
  ModalBodyComponent, ModalComponent, ModalFooterComponent, ModalHeaderComponent,
  SpinnerComponent, TableDirective
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { ApiToken, ApiTokenService } from '../../core/services/api-token.service';

@Component({
  selector: 'app-api-tokens',
  standalone: true,
  imports: [
    ReactiveFormsModule, FormDirective, FormControlDirective, FormLabelDirective, FormSelectDirective, InputGroupComponent,
    ContainerComponent, RowComponent, ColComponent,
    CardComponent, CardBodyComponent,
    ButtonDirective, SpinnerComponent, AlertComponent, TableDirective,
    ModalComponent, ModalHeaderComponent, ModalBodyComponent, ModalFooterComponent,
    IconDirective, DatePipe
  ],
  templateUrl: './api-tokens.component.html'
})
export class ApiTokensComponent implements OnInit {
  private apiTokenService = inject(ApiTokenService);
  private readonly destroyRef = inject(DestroyRef);
  private fb = inject(FormBuilder);

  enabled             = signal(true);
  tokens              = signal<ApiToken[]>([]);
  loading             = signal(true);
  creating            = signal(false);
  createModalVisible  = signal(false);
  revealModalVisible  = signal(false);
  newTokenValue       = signal<string | null>(null);
  error               = signal<string | null>(null);

  form = this.fb.group({
    name: ['', Validators.required],
    expiresInDays: [null as number | null]
  });

  ngOnInit(): void {
    this.apiTokenService.isEnabled()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(enabled => {
        this.enabled.set(enabled);
        if (enabled) this.load(); else this.loading.set(false);
      });
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.apiTokenService.getAll()
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.loading.set(false)))
      .subscribe({
        next: tokens => this.tokens.set(tokens),
        error: err => this.error.set(err?.error?.message ?? 'Failed to load tokens.')
      });
  }

  openCreateModal(): void {
    this.form.reset({ name: '', expiresInDays: null });
    this.error.set(null);
    this.createModalVisible.set(true);
  }

  create(): void {
    if (this.form.invalid) return;
    const { name, expiresInDays } = this.form.getRawValue();

    this.creating.set(true);
    this.error.set(null);

    this.apiTokenService.create(name!, expiresInDays ?? null)
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.creating.set(false)))
      .subscribe({
        next: created => {
          this.createModalVisible.set(false);
          this.newTokenValue.set(created.token);
          this.revealModalVisible.set(true);
          this.load();
        },
        error: err => this.error.set(err?.error?.message ?? 'Failed to create token.')
      });
  }

  closeRevealModal(): void {
    this.newTokenValue.set(null);
    this.revealModalVisible.set(false);
  }

  // CoreUI's c-modal re-emits (visibleChange) on every visibility change, including when the parent
  // opens it programmatically (not just when the user closes it). The emitted value must be relayed
  // as-is, otherwise the modal closes itself immediately after opening.
  onRevealModalVisibleChange(visible: boolean): void {
    this.revealModalVisible.set(visible);
    if (!visible) this.newTokenValue.set(null);
  }

  copyToken(): void {
    const value = this.newTokenValue();
    if (value) navigator.clipboard?.writeText(value);
  }

  delete(token: ApiToken): void {
    if (!confirm(`Delete token "${token.name}"? This action is permanent.`)) return;

    this.apiTokenService.delete(token.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.load(),
        error: err => this.error.set(err?.error?.message ?? 'Failed to delete token.')
      });
  }
}
