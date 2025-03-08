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
  loaderType: 'single-song' | 'playlist' = 'single-song';

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
        title: 'Oops! No URL Detected',
        confirmationText: 'You forgot to enter a URL! How do you expect me to fetch something from nothing?',
        showCancelButton: false
      }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe();
      return;
    }

    // Reset previous content
    this.resourceInformation = undefined;
    this.mediaType = undefined;

    // Show loader
    this.isLoading = true;

    this.fetchService
      .getMediaType(this.url)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (mediaType) => {
          this.mediaType = mediaType;
          if(mediaType == MediaType.NUMBER_0)
            this.loaderType = 'single-song';
          else
            this.loaderType = 'playlist';
        },
        error: () => {
          this.handleError();
        }
      });

    this.fetchService
      .getResource(this.url)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (resourceInformation) => {
          this.resourceInformation = resourceInformation;
          this.isLoading = false;
        },
        error: () => {
          this.handleError();
        }
      });
  }

  private handleError() {
    this.isLoading = false;
    this.modalService.open({
      title: 'Oh no..',
      confirmationText: 'Unable to fetch content. Please check the URL and try again.',
      showCancelButton: false
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe();
  }
}