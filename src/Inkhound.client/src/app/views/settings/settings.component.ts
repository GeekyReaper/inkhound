import { Component, inject, OnInit, signal } from '@angular/core';
import { NavModule } from '@coreui/angular';
import { OptionsService } from '../../core/services/options.service';

@Component({
  selector: 'app-settings',
  standalone: true,
  imports: [NavModule],
  templateUrl: './settings.component.html'
})
export class SettingsComponent implements OnInit {
  private optionsService = inject(OptionsService);

  services = signal<string[]>([]);
  activeTab = signal<string>('');

  ngOnInit() {
    this.optionsService.getServices().subscribe({
      next: names => {
        this.services.set(names);
        if (names.length) this.activeTab.set(names[0]);
      },
      error: () => {}
    });
  }
}
