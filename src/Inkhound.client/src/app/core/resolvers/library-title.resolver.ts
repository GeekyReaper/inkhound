import { inject } from '@angular/core';
import { ResolveFn } from '@angular/router';
import { catchError, map, of } from 'rxjs';
import { LibraryService } from '../services/library.service';

export const libraryTitleResolver: ResolveFn<string> = (route) =>
  inject(LibraryService).getById(route.paramMap.get('id')!).pipe(
    map(lib => lib.name),
    catchError(() => of('Library'))
  );
