import { Injectable, inject, signal } from '@angular/core';
import { HttpClient, HttpEventType } from '@angular/common/http';

/**
 * Downloads one file at a time and reports how it is going.
 * <p>
 * Held in a service rather than a component because two places start downloads now - a single
 * result and a row of a playlist - and the progress belongs to the transfer rather than to
 * whichever view began it.
 */
@Injectable({ providedIn: 'root' })
export class MediaDownloadService {
  private readonly http = inject(HttpClient);

  // Signals: every one of these is written from HTTP progress events, which schedule no change
  // detection of their own in a zoneless application.
  readonly isDownloading = signal(false);
  readonly progress = signal(0);
  readonly fileName = signal('');
  readonly speed = signal('');
  readonly totalMB = signal('');
  readonly downloadedMB = signal('');
  readonly hasKnownTotal = signal(false);

  private lastTime = 0;
  private lastBytes = 0;

  /** Whether a download is already running, which both callers refuse to interrupt. */
  get busy(): boolean {
    return this.isDownloading();
  }

  download(url: string, fileName: string): Promise<void> {
    return new Promise<void>(resolve => {
      this.reset();
      this.isDownloading.set(true);
      this.fileName.set(fileName);
      this.downloadedMB.set('0');
      this.lastTime = Date.now();

      // Errors arrive on the error callback rather than as a thrown exception; subscribe()
      // returns as soon as the request is issued.
      this.http.get(url, { responseType: 'blob', reportProgress: true, observe: 'events' }).subscribe({
        next: event => {
          if (event.type === HttpEventType.DownloadProgress) {
            this.report(event.loaded, event.total);
          } else if (event.type === HttpEventType.Response) {
            if (event.body) {
              this.save(event.body, fileName);
            }

            this.reset();
            resolve();
          }
        },
        error: error => {
          console.error('Download failed:', error);
          this.reset();
          resolve();
        }
      });
    });
  }

  private report(loaded: number, total?: number) {
    // A percentage is only reported when the real transfer size is known. The combined endpoint
    // muxes on the fly and genuinely cannot send a length; there the bar runs indeterminate and
    // the byte counter carries the information.
    this.hasKnownTotal.set(!!total);

    if (total) {
      this.progress.set(Math.min(100, Math.max(0, Math.round((loaded / total) * 100))));
      this.totalMB.set((total / 1024 / 1024).toFixed(1));
    } else {
      this.progress.set(0);
      this.totalMB.set('');
    }

    this.downloadedMB.set((loaded / 1024 / 1024).toFixed(1));

    const now = Date.now();
    const elapsed = (now - this.lastTime) / 1000;

    // Recalculated twice a second: often enough to look live, seldom enough to stay readable.
    if (elapsed > 0.5) {
      this.speed.set(MediaDownloadService.formatSpeed((loaded - this.lastBytes) / elapsed));
      this.lastTime = now;
      this.lastBytes = loaded;
    }
  }

  private save(blob: Blob, fileName: string) {
    const url = window.URL.createObjectURL(blob);
    const link = document.createElement('a');

    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    window.URL.revokeObjectURL(url);
  }

  private reset() {
    this.isDownloading.set(false);
    this.progress.set(0);
    this.fileName.set('');
    this.speed.set('');
    this.totalMB.set('');
    this.downloadedMB.set('');
    this.hasKnownTotal.set(false);
    this.lastBytes = 0;
  }

  private static formatSpeed(bytesPerSecond: number): string {
    if (bytesPerSecond < 1024) {
      return `${bytesPerSecond.toFixed(0)} B/s`;
    }

    if (bytesPerSecond < 1024 * 1024) {
      return `${(bytesPerSecond / 1024).toFixed(1)} KB/s`;
    }

    return `${(bytesPerSecond / 1024 / 1024).toFixed(1)} MB/s`;
  }
}
