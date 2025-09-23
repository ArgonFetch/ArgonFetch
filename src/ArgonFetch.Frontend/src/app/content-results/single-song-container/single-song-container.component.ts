import { Component, Input, HostListener, ElementRef } from '@angular/core';
import { FontAwesomeModule } from '@fortawesome/angular-fontawesome';
import { CommonModule } from '@angular/common';
import { faDownload, faChevronRight, faSpinner } from '@fortawesome/free-solid-svg-icons';
import { ResourceInformationDto } from '../../api';
import { HttpClient, HttpEventType, HttpClientModule } from '@angular/common/http';

@Component({
  selector: 'app-single-song-container',
  standalone: true,
  imports: [FontAwesomeModule, CommonModule, HttpClientModule],
  templateUrl: './single-song-container.component.html',
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
  downloadedMB = '';

  constructor(
    private elementRef: ElementRef,
    private http: HttpClient
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
      const videoUrls = mediaItem.video;
      if (!videoUrls) {
        console.error('No video URLs available');
        return;
      }

      switch (quality) {
        case 'best':
          url = videoUrls.bestQuality;
          extension = videoUrls.bestQualityFileExtension || '.mp4';
          break;
        case 'medium':
          url = videoUrls.mediumQuality;
          extension = videoUrls.mediumQualityFileExtension || '.mp4';
          break;
        case 'worst':
          url = videoUrls.worstQuality;
          extension = videoUrls.worstQualityFileExtension || '.mp4';
          break;
      }
    } else if (type === 'audio') {
      // Handle audio-only streams
      const audioUrls = mediaItem.audio;
      if (!audioUrls) {
        console.error('No audio URLs available');
        return;
      }

      switch (quality) {
        case 'best':
          url = audioUrls.bestQuality;
          extension = audioUrls.bestQualityFileExtension || '.mp3';
          break;
        case 'medium':
          url = audioUrls.mediumQuality;
          extension = audioUrls.mediumQualityFileExtension || '.mp3';
          break;
        case 'worst':
          url = audioUrls.worstQuality;
          extension = audioUrls.worstQualityFileExtension || '.mp3';
          break;
      }
    }

    if (!url) {
      console.error('No URL available for the selected quality and type');
      return;
    }

    // Close menus and start download
    this.closeAllMenus();
    this.currentDownloadName = `${filename}${extension}`;
    // Pass the type and quality to downloadFile for better size estimation
    await this.downloadFile(url, this.currentDownloadName, type, quality);
  }

  private async downloadFile(url: string, filename: string, type: 'combined' | 'audio' = 'combined', quality: 'best' | 'medium' | 'worst' = 'medium') {
    this.isDownloading = true;
    this.downloadProgress = 0;
    this.downloadSpeed = '';
    this.lastDownloadTime = Date.now();
    this.lastDownloadBytes = 0;
    this.currentDownloadName = filename;
    this.totalBytes = 0;
    this.downloadedMB = '0';

    try {
      // Use HttpClient to download with progress tracking
      this.http.get(url, {
        responseType: 'blob',
        reportProgress: true,
        observe: 'events'
      }).subscribe({
        next: (event) => {
          if (event.type === HttpEventType.DownloadProgress) {
            // Store total bytes if available
            this.totalBytes = event.total || 0;

            // Calculate download progress
            if (event.total) {
              // Content-Length is known, show real progress
              const progress = Math.round((event.loaded / event.total) * 100);
              // Clamp progress between 0 and 100
              this.downloadProgress = Math.min(100, Math.max(0, progress));
            } else {
              // Content-Length is unknown, estimate based on typical file sizes
              // Use smarter estimation based on content type and quality
              let estimatedSize: number;

              if (type === 'audio') {
                // Audio files vary by quality
                switch (quality) {
                  case 'best':
                    estimatedSize = 15 * 1024 * 1024; // 15MB for best audio
                    break;
                  case 'medium':
                    estimatedSize = 10 * 1024 * 1024; // 10MB for medium audio
                    break;
                  case 'worst':
                    estimatedSize = 5 * 1024 * 1024; // 5MB for low audio
                    break;
                  default:
                    estimatedSize = 10 * 1024 * 1024;
                }
              } else {
                // Video files (combined) vary significantly by quality
                switch (quality) {
                  case 'best':
                    estimatedSize = 100 * 1024 * 1024; // 100MB for best video
                    break;
                  case 'medium':
                    estimatedSize = 30 * 1024 * 1024; // 30MB for medium video
                    break;
                  case 'worst':
                    estimatedSize = 10 * 1024 * 1024; // 10MB for low video
                    break;
                  default:
                    estimatedSize = 30 * 1024 * 1024;
                }
              }

              // Calculate progress but cap at 95% until download actually completes
              const estimatedProgress = Math.min(95, Math.round((event.loaded / estimatedSize) * 100));
              this.downloadProgress = estimatedProgress;
            }

            // Always update MB downloaded
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
            this.isDownloading = false;
            this.downloadProgress = 0;
            this.currentDownloadName = '';
            this.downloadSpeed = '';
            this.totalBytes = 0;
            this.downloadedMB = '';
          }
        },
        error: (error) => {
          console.error('Download failed:', error);
          this.isDownloading = false;
          this.downloadProgress = 0;
          this.currentDownloadName = '';
          this.downloadSpeed = '';
          this.totalBytes = 0;
          this.downloadedMB = '';
        }
      });
    } catch (error) {
      console.error('Download error:', error);
      this.isDownloading = false;
      this.downloadProgress = 0;
      this.currentDownloadName = '';
      this.downloadSpeed = '';
      this.totalBytes = 0;
      this.downloadedMB = '';
    }
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