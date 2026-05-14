import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-library-shell',
  template: '<router-outlet />',
  imports: [RouterOutlet]
})
export class LibraryShellComponent {}
