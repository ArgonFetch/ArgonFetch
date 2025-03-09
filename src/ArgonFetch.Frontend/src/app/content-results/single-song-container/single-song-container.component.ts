import { Component, DestroyRef, Input } from '@angular/core';
import { FontAwesomeModule } from '@fortawesome/angular-fontawesome';
import { CommonModule } from '@angular/common';
import { faDownload, faSpinner } from '@fortawesome/free-solid-svg-icons';
import { ProxyService, ResourceInformationDto } from '../../../../api';
import { firstValueFrom } from 'rxjs';

@Component({
  selector: 'app-single-song-container',
  standalone: true,
  imports: [FontAwesomeModule, CommonModule],
  templateUrl: './single-song-container.component.html',
  styleUrl: './single-song-container.component.scss'
})
export class SingleSongContainerComponent {
  @Input() resourceInformation!: ResourceInformationDto;

  faDownload = faDownload;
  faSpinner = faSpinner;
  isDownloading = false;
  downloadProgress = 0;
  downloadSpeed = 0; // Speed in bytes per second
  downloadSpeedText = ''; // Formatted speed (KB/s or MB/s)
  estimatedTimeText = ''; // Formatted estimated time

  // For speed calculation
  private startTime = 0;
  private loadedBytes = 0;
  private totalBytes = 0;
  private speedUpdateInterval: any;

  // Parallel download settings
  private readonly CHUNK_COUNT = 4; // Number of parallel connections
  private chunks: { start: number; end: number; loaded: number; blob?: Blob }[] = [];
  private activeRequests = 0;

  constructor(private proxyService: ProxyService) {}

  formatSpeed(bytesPerSecond: number): string {
    if (bytesPerSecond > 1048576) { // 1 MB/s
      return (bytesPerSecond / 1048576).toFixed(2) + ' MB/s';
    } else {
      return (bytesPerSecond / 1024).toFixed(2) + ' KB/s';
    }
  }

  formatTime(seconds: number): string {
    if (seconds === Infinity || isNaN(seconds)) {
      return 'calculating...';
    }

    if (seconds < 60) {
      return Math.ceil(seconds) + ' sec';
    } else if (seconds < 3600) {
      const minutes = Math.floor(seconds / 60);
      const remainingSeconds = Math.ceil(seconds % 60);
      return `${minutes}m ${remainingSeconds}s`;
    } else {
      const hours = Math.floor(seconds / 3600);
      const minutes = Math.floor((seconds % 3600) / 60);
      return `${hours}h ${minutes}m`;
    }
  }

  updateSpeed(): void {
    const currentTime = Date.now();
    const elapsedSeconds = (currentTime - this.startTime) / 1000;

    if (elapsedSeconds > 0) {
      this.downloadSpeed = this.loadedBytes / elapsedSeconds;
      this.downloadSpeedText = this.formatSpeed(this.downloadSpeed);

      // Calculate remaining time
      const remainingBytes = this.totalBytes - this.loadedBytes;
      const estimatedSeconds = remainingBytes / this.downloadSpeed;
      this.estimatedTimeText = this.formatTime(estimatedSeconds);
    }
  }

  async onDownload() {
    if (this.isDownloading) return;

    this.resetDownloadState();

    const url = this.resourceInformation.mediaItems?.[0].streamingUrl;
    const filename = this.resourceInformation.mediaItems?.[0].title + '.mp3';

    if (!url) {
      console.error('No streaming URL available');
      return;
    }

    this.isDownloading = true;
    this.startTime = Date.now();

    try {
      const headResponse = await firstValueFrom(this.proxyService.proxyHead(url));

      this.totalBytes = headResponse.contentLength ?? 0;

      if (!this.totalBytes) {
        throw new Error("Content-Length header is missing");
      }

      const chunkSize = Math.floor(this.totalBytes / this.CHUNK_COUNT);
      this.chunks = [];

      for (let i = 0; i < this.CHUNK_COUNT; i++) {
        const start = i * chunkSize;
        const end = (i === this.CHUNK_COUNT - 1) ? this.totalBytes - 1 : start + chunkSize - 1;
        this.chunks.push({ start, end, loaded: 0 });
      }

      this.speedUpdateInterval = setInterval(() => this.updateSpeed(), 1000);

      const downloadPromises = this.chunks.map((chunk, index) =>
        this.downloadChunk(url, chunk, index)
      );

      await Promise.all(downloadPromises);

      if (this.chunks.every(chunk => chunk.blob)) {
        const completeBlob = new Blob(
          this.chunks.map(chunk => chunk.blob as Blob),
          { type: 'audio/mpeg' }
        );

        const link = document.createElement("a");
        link.href = URL.createObjectURL(completeBlob);
        link.download = filename;
        link.click();
        URL.revokeObjectURL(link.href);
      }
    } catch (error) {
      console.error("Download failed:", error);
    } finally {
      this.isDownloading = false;
      clearInterval(this.speedUpdateInterval);
    }
  }

  async downloadChunk(url: string, chunk: { start: number; end: number; loaded: number; blob?: Blob }, chunkIndex: number): Promise<void> {
    this.activeRequests++;

    try {
      const response = await firstValueFrom(
        this.proxyService.proxyRange(url, chunk.start, chunk.end)
      );

      chunk.blob = response;
      chunk.loaded = response.size;
      this.loadedBytes += response.size;
      this.downloadProgress = Math.round((this.loadedBytes / this.totalBytes) * 100);
    } catch (error) {
      console.error(`Error downloading chunk ${chunkIndex}:`, error);
      throw error;
    } finally {
      this.activeRequests--;
    }
  }

  private resetDownloadState(): void {
    this.isDownloading = false;
    this.downloadProgress = 0;
    this.loadedBytes = 0;
    this.totalBytes = 0;
    this.downloadSpeed = 0;
    this.downloadSpeedText = '';
    this.estimatedTimeText = '';
    this.chunks = [];

    if (this.speedUpdateInterval) {
      clearInterval(this.speedUpdateInterval);
      this.speedUpdateInterval = null;
    }
  }

  ngOnDestroy() {
    if (this.speedUpdateInterval) {
      clearInterval(this.speedUpdateInterval);
    }
  }
}