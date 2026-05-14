import { inject } from '@angular/core';
import { ResolveFn } from '@angular/router';
import { catchError, map, of } from 'rxjs';
import { VolumeService } from '../services/volume.service';

export const volumeTitleResolver: ResolveFn<string> = (route) =>
  inject(VolumeService).getById(route.paramMap.get('volumeId')!).pipe(
    map(v => v.title),
    catchError(() => of('Volume'))
  );
