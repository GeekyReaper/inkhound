import { Component } from '@angular/core';
import { ContainerComponent } from '@coreui/angular';
import { VolumeSearchComponent } from '../volume/volume-search/volume-search.component';
import { VolumeRequestListComponent } from './volume-request-list.component';

// Page /requests (fusion des anciennes pages Add Volume et Requests) : section « Requests » (liste
// des demandes) puis section « New request » (recherche et ajout/demande d'un volume).
@Component({
  selector: 'app-volume-requests',
  templateUrl: './volume-requests.component.html',
  imports: [ContainerComponent, VolumeRequestListComponent, VolumeSearchComponent]
})
export class VolumeRequestsComponent {}
