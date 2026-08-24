import { Component, Input, ChangeDetectionStrategy, signal } from '@angular/core';
import { CdkMenu, CdkMenuItem, CdkMenuTrigger } from '@angular/cdk/menu';
import { FontAwesomeModule } from '@fortawesome/angular-fontawesome';

import { faDownload, faChevronRight, faSpinner } from '@fortawesome/free-solid-svg-icons';
import { MediaRenditionDto, ResourceInformationDto } from '../../api';
import { HttpClient, HttpEventType, HttpClientModule } from '@angular/common/http';
import { ResourceUrlService } from '../../services/resource-url.service';

@Component({
  selector: 'app-single-song-container',
  standalone: true,
  imports: [FontAwesomeModule, HttpClientModule, CdkMenu, CdkMenuItem, CdkMenuTrigger],
  templateUrl: './single-song-container.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './single-song-container.component.scss'
})
export class SingleSongContainerComponent {
  @Input() resourceInformation!: ResourceInformationDto;

  faDownload = faDownload;
  faChevronRight = faChevronRight;
  faSpinner = faSpinner;

  // Download progress, all of it written from HTTP progress events. Those schedule no
  // change detection without zone.js, so the view reads signals instead of fields.
  isDownloading = signal(false);
  downloadProgress = signal(0);
  currentDownloadName = signal('');
  downloadSpeed = signal('');
  totalMB = signal('');
  hasKnownTotal = signal(false);
  downloadedMB = signal('');

  // Not rendered - only used to work out the speed between two events.
  private lastDownloadTime = 0;
  private lastDownloadBytes = 0;

  constructor(
    private http: HttpClient,
    private resourceUrlService: ResourceUrlService
  ) {}

  /** Renditions the server offers, best first. Empty for sources that report none. */
  videoRenditions(): MediaRenditionDto[] {
    return this.resourceInformation.mediaItems?.[0]?.video?.renditions ?? [];
  }

  audioRenditions(): MediaRenditionDto[] {
    return this.resourceInformation.mediaItems?.[0]?.audio?.renditions ?? [];
  }

  /**
   * Container of a rendition, e.g. "WEBM". Worth showing beside the quality: two renditions
   * can be the same bitrate and still not play in the same places.
   */
  typeOf(rendition: MediaRenditionDto): string {
    return (rendition.fileExtension || '').replace('.', '').toUpperCase();
  }

  /** Size label for a rendition, e.g. "3.3 MB". Empty when the source does not report one. */
  sizeOf(rendition: MediaRenditionDto): string {
    return this.resourceUrlService.formatSize(rendition.fileSizeBytes);
  }

  /** Downloads one rendition, converted or passed through as the server described it. */
  async onDownloadRendition(rendition: MediaRenditionDto) {
    if (this.isDownloading()) {
      return;
    }

    const url = this.resourceUrlService.buildRenditionUrl(rendition);
    if (!url) {
      console.error('No URL available for the selected rendition');
      return;
    }

    const title = this.resourceInformation.mediaItems?.[0]?.title || 'download';
    const extension = rendition.fileExtension || '';

    await this.downloadFile(url, `${title}${extension}`);
  }

  async onDownload(quality: 'best' | 'medium' | 'worst', type: 'combined' | 'audio') {
    if (this.isDownloading()) {
      return; // Prevent multiple downloads at once
    }

    const mediaItem = this.resourceInformation.mediaItems?.[0];
    if (!mediaItem) {
      console.error('No media item available');
      return;
    }

    let url: string | null | undefined = null;
    let filename = mediaItem.title || 'download';
    let extension = '';

    if (type === 'combined') {
      // Handle video streams (with audio)
      const videoRef = mediaItem.video;
      if (!videoRef) {
        console.error('No video references available');
        return;
      }

      url = this.resourceUrlService.buildResourceUrl(videoRef, quality);
      extension = this.resourceUrlService.getFileExtension(videoRef, quality) || '.mp4';
    } else if (type === 'audio') {
      // Handle audio-only streams
      const audioRef = mediaItem.audio;
      if (!audioRef) {
        console.error('No audio references available');
        return;
      }

      url = this.resourceUrlService.buildResourceUrl(audioRef, quality);
      extension = this.resourceUrlService.getFileExtension(audioRef, quality) || '.mp3';
    }

    if (!url) {
      console.error('No URL available for the selected quality and type');
      return;
    }

    this.currentDownloadName.set(`${filename}${extension}`);
    await this.downloadFile(url, this.currentDownloadName());
  }

  private async downloadFile(url: string, filename: string) {
    this.isDownloading.set(true);
    this.downloadProgress.set(0);
    this.downloadSpeed.set('');
    this.lastDownloadTime = Date.now();
    this.lastDownloadBytes = 0;
    this.currentDownloadName.set(filename);
    this.totalMB.set('');
    this.hasKnownTotal.set(false);
    this.downloadedMB.set('0');

    // Use HttpClient to download with progress tracking.
    // Errors arrive on the error callback, not as a thrown exception - subscribe()
    // returns as soon as the request is issued.
    this.http.get(url, {
      responseType: 'blob',
      reportProgress: true,
      observe: 'events'
    }).subscribe({
        next: (event) => {
          if (event.type === HttpEventType.DownloadProgress) {
            // Only report a percentage when the real transfer size is known. The size used
            // to be guessed from hardcoded per-quality figures, which produced a bar that
            // bore no relation to the transfer and then sat at 95% until it finished.
            // The combined endpoint muxes on the fly, so it genuinely cannot send a length;
            // there the bar runs indeterminate and the byte counter carries the information.
            this.hasKnownTotal.set(!!event.total);

            if (event.total) {
              const progress = Math.round((event.loaded / event.total) * 100);
              this.downloadProgress.set(Math.min(100, Math.max(0, progress)));
              this.totalMB.set((event.total / 1024 / 1024).toFixed(1));
            } else {
              this.downloadProgress.set(0);
              this.totalMB.set('');
            }

            // Always truthful, known on every event, and the only figure available when
            // the server cannot declare a length.
            this.downloadedMB.set((event.loaded / 1024 / 1024).toFixed(1));

            // Calculate download speed
            const currentTime = Date.now();
            const timeDiff = (currentTime - this.lastDownloadTime) / 1000; // in seconds

            if (timeDiff > 0.5) { // Update speed every 0.5 seconds
              const bytesDiff = event.loaded - this.lastDownloadBytes;
              const speed = bytesDiff / timeDiff; // bytes per second
              this.downloadSpeed.set(this.formatSpeed(speed));

              this.lastDownloadTime = currentTime;
              this.lastDownloadBytes = event.loaded;
            }
          } else if (event.type === HttpEventType.Response) {
            // Download complete, save the file
            const blob = event.body;
            if (blob) {
              this.saveBlob(blob, filename);
            }
            this.resetDownloadState();
          }
        },
        error: (error) => {
          console.error('Download failed:', error);
          this.resetDownloadState();
        }
      });
  }

  private resetDownloadState() {
    this.isDownloading.set(false);
    this.downloadProgress.set(0);
    this.totalMB.set('');
    this.hasKnownTotal.set(false);
    this.currentDownloadName.set('');
    this.downloadSpeed.set('');
    this.downloadedMB.set('');
  }

  private saveBlob(blob: Blob, filename: string) {
    const url = window.URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = filename;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    window.URL.revokeObjectURL(url);
  }

  hasCombinedUrls(): boolean {
    return !!this.resourceInformation.mediaItems?.[0]?.video;
  }

  hasAudioUrls(): boolean {
    return !!this.resourceInformation.mediaItems?.[0]?.audio;
  }

  private formatSpeed(bytesPerSecond: number): string {
    if (bytesPerSecond < 1024) {
      return `${bytesPerSecond.toFixed(0)} B/s`;
    } else if (bytesPerSecond < 1024 * 1024) {
      return `${(bytesPerSecond / 1024).toFixed(1)} KB/s`;
    } else {
      return `${(bytesPerSecond / 1024 / 1024).toFixed(1)} MB/s`;
    }
  }
}