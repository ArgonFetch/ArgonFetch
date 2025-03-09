import { Component, DestroyRef } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { FontAwesomeModule } from '@fortawesome/angular-fontawesome';
import { faTriangleExclamation } from '@fortawesome/free-solid-svg-icons';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { faLink, faSearch, faTimes } from '@fortawesome/free-solid-svg-icons';
import { PlaylistContainerComponent } from '../content-results/playlist-container/playlist-container.component';
import { ModalService } from '../services/modal.service';
import { ContentSkeletonLoaderComponent } from "../content-skeleton-loader/content-skeleton-loader.component";
import { SingleSongContainerComponent } from '../content-results/single-song-container/single-song-container.component';
import { FetchService, MediaType, ResourceInformationDto } from '../../../api';

@Component({
  selector: 'app-home',
  templateUrl: './home.component.html',
  styleUrls: ['./home.component.scss'],
  standalone: true,
  imports: [
    FormsModule,
    CommonModule,
    FontAwesomeModule,
    PlaylistContainerComponent,
    SingleSongContainerComponent,
    ContentSkeletonLoaderComponent
  ]
})
export class HomeComponent {
  faLink = faLink;
  faSearch = faSearch;
  faTimes = faTimes;
  mediaTypeEnum = MediaType;

  url: string = '';
  faWarning = faTriangleExclamation;

  isLoading: boolean = false;
  loaderType: 'single-song' | 'playlist' | 'unknown' = 'unknown';

  resourceInformation: ResourceInformationDto | undefined;
  mediaType: MediaType | undefined;

  constructor(
    private fetchService: FetchService,
    private destroyRef: DestroyRef,
    private modalService: ModalService
  ) { }

  async download() {
    if (!this.url) {
      this.modalService.open({
        title: 'Yoooo! No URL Detected',
        confirmationText: 'You forgot to enter a URL! How do you expect me to fetch something from nothing?',
        showCancelButton: false
      }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe();
      return;
    }

    // Reset previous content
    this.resourceInformation = undefined;

    // Show loader
    this.isLoading = true;

    this.fetchService
      .getMediaType(this.url)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (mediaType) => {
          if (mediaType == MediaType.NUMBER_0) {
            this.loaderType = 'single-song';
            this.fetchResource();
          } else if (mediaType == MediaType.NUMBER_1) {
            this.loaderType = 'playlist';
            this.handleError('Hold on a minute...', 'Young padawan, I have to sadly inform you that the playlist feature isn\'t available yet.');
          } else {
            this.loaderType = 'unknown';
            this.fetchResource();
          }
        },
        error: () => {
          this.handleError('Well, this is awkward...', 'Something unexpected happened. Mind giving it another shot?');
        }
      });
  }

  private fetchResource() {
    this.fetchService
      .getResource(this.url)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (resourceInformation) => {
          resourceInformation.type
          this.resourceInformation = resourceInformation;
          console.log(resourceInformation.type);
          this.isLoading = false;
        },
        error: (error) => {
          if (error.status === 404) {
            this.handleError('Resource Not Found', 'We couldn\'t find what you\'re looking for. Are you sure that URL is correct?');
          } else if (error.status === 415) {
            this.handleError('Hold on a minute...', 'Young padawan, I have to sadly inform you that the playlist feature isn\'t available yet.');
          } else if (error.status === 502) {
            this.handleError('Fetch Failed', 'We couldn\'t reach the source. Please try again later.');
          } else {
            this.handleError('Well, this is awkward...', 'Something unexpected happened. Mind giving it another shot?');
          }
        }
      });
  }

  private handleError(title: string, confirmationText: string) {
    this.isLoading = false;
    this.modalService.open({
      title: title,
      confirmationText: confirmationText,
      showCancelButton: false
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe();
  }
}