import { inject } from '@angular/core';
import { ResolveFn } from '@angular/router';
import { catchError, map, of } from 'rxjs';
import { IssueService } from '../services/issue.service';

export const issueTitleResolver: ResolveFn<string> = (route) =>
  inject(IssueService).getById(route.paramMap.get('issueId')!).pipe(
    map(i => i.title ? `#${i.issueNumber} — ${i.title}` : `Issue #${i.issueNumber}`),
    catchError(() => of('Issue'))
  );
