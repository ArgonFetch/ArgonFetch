import { Component, Input } from '@angular/core';
import { FontAwesomeModule } from '@fortawesome/angular-fontawesome';
import { faDownload } from '@fortawesome/free-solid-svg-icons';
import { CommonModule } from '@angular/common';
import { ResourceInformationDto } from '../../../../api';

@Component({
  selector: 'app-playlist-container',
  standalone: true,
  imports: [FontAwesomeModule, CommonModule],
  templateUrl: './playlist-container.component.html',
  styleUrl: './playlist-container.component.scss'
})
export class PlaylistContainerComponent {
  @Input() resourceInformation!: ResourceInformationDto;
  
  faDownload = faDownload;

  onDownload() {
    // TODO: Implement playlist download functionality
  }
}
