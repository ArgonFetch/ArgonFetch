import { Link, Search, X, TriangleAlert } from 'lucide';
import { IconComponent } from '../icon/icon.component';
import { Component, DestroyRef, ChangeDetectionStrategy, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { PlaylistContainerComponent } from '../content-results/playlist-container/playlist-container.component';
import { NotificationService } from '../notifications/notification.service';
import { ContentSkeletonLoaderComponent } from "../content-skeleton-loader/content-skeleton-loader.component";
import { SingleSongContainerComponent } from '../content-results/single-song-container/single-song-container.component';
import { MediaType, ResourceInformationDto, FetchService } from '../api';

@Component({
  selector: 'app-home',
  templateUrl: './home.component.html',
  styleUrls: ['./home.component.scss'],
  standalone: true,
  changeDetection: ChangeDetectionStrategy.Eager,
  imports: [
    FormsModule,
    IconComponent,
    PlaylistContainerComponent,
    SingleSongContainerComponent,
    ContentSkeletonLoaderComponent
]
})
export class HomeComponent {
  linkIcon = Link;
  searchIcon = Search;
  clearIcon = X;
  mediaTypeEnum = MediaType;

  url: string = '';
  warningIcon = TriangleAlert;

  // Signals: every one of these is written from an HTTP subscribe, which schedules no
  // change detection of its own now that the app runs without zone.js.
  isLoading = signal(false);
  loaderType = signal<'single-song' | 'playlist' | 'unknown'>('unknown');

  resourceInformation = signal<ResourceInformationDto | undefined>(undefined);
  mediaType = signal<MediaType | undefined>(undefined);

  /**
   * Whether the page is showing a result, or about to.
   * <p>
   * Until it is, this is a landing page and the name belongs in the middle of it. Once a
   * result arrives the name has been read, and the room it takes is room the listing wants.
   */
  hasResult(): boolean {
    return this.isLoading() || !!this.resourceInformation();
  }

  constructor(
    private fetchService: FetchService,
    private destroyRef: DestroyRef,
    private notifications: NotificationService
  ) { }

  async download() {
    // Enter can fire while a fetch is already running; the Search button is disabled
    // for the same reason.
    if (this.isLoading()) {
      return;
    }

    if (!this.url) {
      this.notifications.show({
        title: 'No URL detected',
        message: 'Enter a URL to fetch something.',
        tone: 'error'
      });
      return;
    }

    // Reset previous content
    this.resourceInformation.set(undefined);

    // Show loader
    this.isLoading.set(true);
    this.loaderType.set('single-song'); // Default to single-song loader

    this.fetchResource();
  }

  private fetchResource() {
    this.fetchService
      .getResource(this.url)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (resourceInformation: ResourceInformationDto) => {
          this.resourceInformation.set(resourceInformation);
          this.mediaType.set(resourceInformation.type);

          // Update loader type based on actual media type
          this.loaderType.set(resourceInformation.type === MediaType.PlayList ? 'playlist' : 'single-song');

          this.isLoading.set(false);
        },
        error: (error: any) => {
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
    this.isLoading.set(false);
    this.notifications.show({ title, message: confirmationText, tone: 'error' });
  }
}