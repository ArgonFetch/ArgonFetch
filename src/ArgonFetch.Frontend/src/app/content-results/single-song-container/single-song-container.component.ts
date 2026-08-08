import { Component, Input, HostListener, ElementRef, ChangeDetectionStrategy } from '@angular/core';
import { FontAwesomeModule } from '@fortawesome/angular-fontawesome';

import { faDownload, faChevronRight, faSpinner } from '@fortawesome/free-solid-svg-icons';
import { ResourceInformationDto } from '../../api';
import { HttpClient, HttpEventType, HttpClientModule } from '@angular/common/http';
import { ResourceUrlService } from '../../services/resource-url.service';

@Component({
  selector: 'app-single-song-container',
  standalone: true,
  imports: [FontAwesomeModule, HttpClientModule],
  templateUrl: './single-song-container.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './single-song-container.component.scss'
})
export class SingleSongContainerComponent {
  @Input() resourceInformation!: ResourceInformationDto;

  faDownload = faDownload;
  faChevronRight = faChevronRight;
  faSpinner = faSpinner;

  showMainMenu = false;
  showVideoSubmenu = false;
  showAudioSubmenu = false;

  // Download progress tracking
  isDownloading = false;
  downloadProgress = 0;
  currentDownloadName = '';
  downloadSpeed = '';
  lastDownloadTime = 0;
  lastDownloadBytes = 0;
  totalBytes = 0;
  totalMB = '';
  hasKnownTotal = false;
  downloadedMB = '';

  constructor(
    private elementRef: ElementRef,
    private http: HttpClient,
    private resourceUrlService: ResourceUrlService
  ) {}

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent) {
    if (!this.elementRef.nativeElement.contains(event.target)) {
      this.closeAllMenus();
    }
  }

  toggleMainMenu(event: Event) {
    event.stopPropagation();
    this.showMainMenu = !this.showMainMenu;
    if (!this.showMainMenu) {
      this.showVideoSubmenu = false;
      this.showAudioSubmenu = false;
    }
  }

  showVideoMenu(event: Event) {
    event.stopPropagation();
    this.showVideoSubmenu = true;
    this.showAudioSubmenu = false;
  }

  showAudioMenu(event: Event) {
    event.stopPropagation();
    this.showAudioSubmenu = true;
    this.showVideoSubmenu = false;
  }

  hideVideoMenu() {
    // Removed timeout to prevent menu from disappearing
    // The menu will stay open when hovering over submenu
  }

  hideAudioMenu() {
    // Removed timeout to prevent menu from disappearing
    // The menu will stay open when hovering over submenu
  }

  closeAllMenus() {
    this.showMainMenu = false;
    this.showVideoSubmenu = false;
    this.showAudioSubmenu = false;
  }

  async onDownload(quality: 'best' | 'medium' | 'worst', type: 'combined' | 'audio', event: Event) {
    event.stopPropagation();

    if (this.isDownloading) {
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

    // Close menus and start download
    this.closeAllMenus();
    this.currentDownloadName = `${filename}${extension}`;
    await this.downloadFile(url, this.currentDownloadName);
  }

  private async downloadFile(url: string, filename: string) {
    this.isDownloading = true;
    this.downloadProgress = 0;
    this.downloadSpeed = '';
    this.lastDownloadTime = Date.now();
    this.lastDownloadBytes = 0;
    this.currentDownloadName = filename;
    this.totalBytes = 0;
    this.totalMB = '';
    this.hasKnownTotal = false;
    this.downloadedMB = '0';

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
            // Store total bytes if available
            this.totalBytes = event.total || 0;

            // Only report a percentage when the real transfer size is known. The size used
            // to be guessed from hardcoded per-quality figures, which produced a bar that
            // bore no relation to the transfer and then sat at 95% until it finished.
            // The combined endpoint muxes on the fly, so it genuinely cannot send a length;
            // there the bar runs indeterminate and the byte counter carries the information.
            this.hasKnownTotal = !!event.total;

            if (event.total) {
              const progress = Math.round((event.loaded / event.total) * 100);
              this.downloadProgress = Math.min(100, Math.max(0, progress));
              this.totalMB = (event.total / 1024 / 1024).toFixed(1);
            } else {
              this.downloadProgress = 0;
              this.totalMB = '';
            }

            // Always truthful, known on every event, and the only figure available when
            // the server cannot declare a length.
            this.downloadedMB = (event.loaded / 1024 / 1024).toFixed(1);

            // Calculate download speed
            const currentTime = Date.now();
            const timeDiff = (currentTime - this.lastDownloadTime) / 1000; // in seconds

            if (timeDiff > 0.5) { // Update speed every 0.5 seconds
              const bytesDiff = event.loaded - this.lastDownloadBytes;
              const speed = bytesDiff / timeDiff; // bytes per second
              this.downloadSpeed = this.formatSpeed(speed);

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
    this.isDownloading = false;
    this.downloadProgress = 0;
    this.totalMB = '';
    this.hasKnownTotal = false;
    this.currentDownloadName = '';
    this.downloadSpeed = '';
    this.totalBytes = 0;
    this.downloadedMB = '';
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