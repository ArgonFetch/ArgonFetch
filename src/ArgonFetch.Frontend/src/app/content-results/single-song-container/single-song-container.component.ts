import { Component, Input, HostListener, ElementRef } from '@angular/core';
import { FontAwesomeModule } from '@fortawesome/angular-fontawesome';
import { CommonModule } from '@angular/common';
import { faDownload, faChevronRight, faSpinner } from '@fortawesome/free-solid-svg-icons';
import { ResourceInformationDto } from '../../api';
import { HttpClient, HttpEventType, HttpClientModule } from '@angular/common/http';
import { ResourceUrlService } from '../../services/resource-url.service';

export enum Quality {
  Best = 'best',
  Medium = 'medium',
  Worst = 'worst'
}

@Component({
  selector: 'app-single-song-container',
  standalone: true,
  imports: [FontAwesomeModule, CommonModule, HttpClientModule],
  templateUrl: './single-song-container.component.html',
  styleUrl: './single-song-container.component.scss'
})
export class SingleSongContainerComponent {
  @Input() resourceInformation!: ResourceInformationDto;

  // Expose enum to template
  Quality = Quality;

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
  totalMB = '';
  estimatedContentLength = 0; // Store estimated size from X-Estimated-Content-Length header

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

  async onDownload(quality: Quality, type: 'combined' | 'audio', event: Event) {
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
    let qualityDescription = '';

    if (type === 'combined') {
      // Handle video streams (with audio)
      const videoRef = mediaItem.video as any; // Cast to any since the API models might not be updated yet
      if (!videoRef) {
        console.error('No video references available');
        return;
      }

      url = this.resourceUrlService.buildResourceUrl(videoRef, quality);
      extension = this.resourceUrlService.getFileExtension(videoRef, quality) || '.mp4';

      // Get quality description based on selected quality
      if (quality === Quality.Best) {
        qualityDescription = videoRef.bestQualityDescription || '';
      } else if (quality === Quality.Medium) {
        qualityDescription = videoRef.mediumQualityDescription || '';
      } else {
        qualityDescription = videoRef.worstQualityDescription || '';
      }
    } else if (type === 'audio') {
      // Handle audio-only streams
      const audioRef = mediaItem.audio as any; // Cast to any since the API models might not be updated yet
      if (!audioRef) {
        console.error('No audio references available');
        return;
      }

      url = this.resourceUrlService.buildResourceUrl(audioRef, quality);
      extension = this.resourceUrlService.getFileExtension(audioRef, quality) || '.mp3';

      // Get quality description based on selected quality
      if (quality === Quality.Best) {
        qualityDescription = audioRef.bestQualityDescription || '';
      } else if (quality === Quality.Medium) {
        qualityDescription = audioRef.mediumQualityDescription || '';
      } else {
        qualityDescription = audioRef.worstQualityDescription || '';
      }
    }

    if (!url) {
      console.error('No URL available for the selected quality and type');
      return;
    }

    // Close menus and start download
    this.closeAllMenus();

    // Extract quality info from description (e.g., "2160p60", "480p", "144p")
    const qualityInfo = this.extractQualityInfo(qualityDescription, type);

    // Full filename for the actual download
    const fullFilename = `${filename} (${qualityInfo})${extension}`;

    // Simplified display name for progress bar (just quality + extension)
    this.currentDownloadName = `${qualityInfo}${extension}`;

    await this.downloadFile(url, fullFilename);
  }

  private extractQualityInfo(description: string, type: 'combined' | 'audio'): string {
    if (!description) {
      return type === 'combined' ? 'Video' : 'Audio';
    }

    // For video: extract resolution like "2160p60", "1080p", "480p", "144p"
    if (type === 'combined') {
      const resolutionMatch = description.match(/(\d{3,4}p\d{0,3})/);
      if (resolutionMatch) {
        return resolutionMatch[1];
      }
      // Fallback: try to extract dimensions like "3840x2160"
      const dimensionsMatch = description.match(/(\d{3,4})x(\d{3,4})/);
      if (dimensionsMatch) {
        const height = parseInt(dimensionsMatch[2]);
        return `${height}p`;
      }
    } else {
      // For audio: extract quality info like "medium", "low", "high"
      const audioQualityMatch = description.match(/audio only \(([^,)]+)/);
      if (audioQualityMatch) {
        const audioQuality = audioQualityMatch[1];
        return `Audio ${audioQuality.charAt(0).toUpperCase() + audioQuality.slice(1)}`;
      }
    }

    return type === 'combined' ? 'Video' : 'Audio';
  }

  private async downloadFile(url: string, filename: string) {
    this.isDownloading = true;
    this.downloadProgress = 0;
    this.downloadSpeed = '';
    this.lastDownloadTime = Date.now();
    this.lastDownloadBytes = 0;
    // currentDownloadName is already set before calling this method - don't overwrite it
    this.totalBytes = 0;
    this.downloadedMB = '';
    this.totalMB = '';
    this.estimatedContentLength = 0;

    try {
      // Use HttpClient to download with progress tracking
      this.http.get(url, {
        responseType: 'blob',
        reportProgress: true,
        observe: 'events'
      }).subscribe({
        next: (event) => {
          // Capture X-Estimated-Content-Length header when response headers are available
          if (event.type === HttpEventType.ResponseHeader) {
            const estimatedLength = event.headers.get('x-estimated-content-length');
            if (estimatedLength) {
              const estimated = parseInt(estimatedLength, 10);
              if (!isNaN(estimated) && estimated > 0) {
                this.estimatedContentLength = estimated;
              }
            }
          }

          if (event.type === HttpEventType.DownloadProgress) {
            // Store total bytes if available
            this.totalBytes = event.total || 0;

            // Calculate download progress
            if (event.total) {
              // Content-Length is known, show real progress
              const progress = Math.round((event.loaded / event.total) * 100);
              this.downloadProgress = Math.min(100, Math.max(0, progress));
              this.totalMB = (event.total / 1024 / 1024).toFixed(1);
            } else if (this.estimatedContentLength > 0) {
              // Use X-Estimated-Content-Length for progress tracking (FFmpeg combined streams)
              const progress = Math.round((event.loaded / this.estimatedContentLength) * 100);
              // Cap at 99% until actual completion (since it's an estimate)
              this.downloadProgress = Math.min(99, Math.max(0, progress));
              this.totalMB = (this.estimatedContentLength / 1024 / 1024).toFixed(1);
            }

            // Always update MB downloaded
            this.downloadedMB = (event.loaded / 1024 / 1024).toFixed(1);

            // Calculate download speed
            const currentTime = Date.now();
            const timeDiff = (currentTime - this.lastDownloadTime) / 1000;

            if (timeDiff > 0.5) {
              const bytesDiff = event.loaded - this.lastDownloadBytes;
              const speed = bytesDiff / timeDiff;
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
            this.totalMB = '';
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
          this.totalMB = '';
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
      this.totalMB = '';
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

  hasVideoQuality(quality: Quality): boolean {
    const mediaItem = this.resourceInformation.mediaItems?.[0];
    if (!mediaItem?.video) {
      return false;
    }

    const videoRef = mediaItem.video as any;
    const key = quality === Quality.Best ? videoRef.bestQualityKey :
                quality === Quality.Medium ? videoRef.mediumQualityKey :
                videoRef.worstQualityKey;

    return !!key;
  }

  hasAudioQuality(quality: Quality): boolean {
    const mediaItem = this.resourceInformation.mediaItems?.[0];
    if (!mediaItem?.audio) {
      return false;
    }

    const audioRef = mediaItem.audio as any;
    const key = quality === Quality.Best ? audioRef.bestQualityKey :
                quality === Quality.Medium ? audioRef.mediumQualityKey :
                audioRef.worstQualityKey;

    return !!key;
  }

  getVideoQuality(quality: Quality): string {
    const mediaItem = this.resourceInformation.mediaItems?.[0];
    if (!mediaItem?.video) {
      return '';
    }

    const videoRef = mediaItem.video as any;
    let description = '';

    if (quality === Quality.Best) {
      description = videoRef.bestQualityDescription || '';
    } else if (quality === Quality.Medium) {
      description = videoRef.mediumQualityDescription || '';
    } else {
      description = videoRef.worstQualityDescription || '';
    }

    return this.extractQualityInfo(description, 'combined');
  }

  getAudioQuality(quality: Quality): string {
    const mediaItem = this.resourceInformation.mediaItems?.[0];
    if (!mediaItem?.audio) {
      return '';
    }

    const audioRef = mediaItem.audio as any;
    let description = '';

    if (quality === Quality.Best) {
      description = audioRef.bestQualityDescription || '';
    } else if (quality === Quality.Medium) {
      description = audioRef.mediumQualityDescription || '';
    } else {
      description = audioRef.worstQualityDescription || '';
    }

    // For audio menu, just show the quality level without "Audio" prefix
    const fullQuality = this.extractQualityInfo(description, 'audio');
    return fullQuality.replace('Audio ', '');
  }
}