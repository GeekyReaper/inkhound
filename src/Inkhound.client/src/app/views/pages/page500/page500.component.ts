import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { HttpClient, HttpStatusCode } from '@angular/common/http';
import { Router } from '@angular/router';
import { EMPTY, Subscription, interval } from 'rxjs';
import { catchError, startWith, switchMap } from 'rxjs/operators';
import { ColComponent, ContainerComponent, RowComponent, SpinnerComponent } from '@coreui/angular';
import { HubService } from '../../../core/services/hub.service';

@Component({
  selector: 'app-page500',
  templateUrl: './page500.component.html',
  imports: [ContainerComponent, RowComponent, ColComponent, SpinnerComponent]
})
export class Page500Component implements OnInit, OnDestroy {
  private http = inject(HttpClient);
  private router = inject(Router);
  private hub = inject(HubService);
  private retrySubscription?: Subscription;

  ngOnInit() {
    this.retrySubscription = interval(3000).pipe(
      startWith(0),
      switchMap(() => this.http.get('/api/auth/me').pipe(
        catchError(err => {
          if (err.status === HttpStatusCode.Unauthorized) {
            this.router.navigate(['/login']);
          }
          return EMPTY;
        })
      ))
    ).subscribe(() => {
      this.hub.ensureConnected();
      this.router.navigate(['/dashboard']);
    });
  }

  ngOnDestroy() {
    this.retrySubscription?.unsubscribe();
  }
}
