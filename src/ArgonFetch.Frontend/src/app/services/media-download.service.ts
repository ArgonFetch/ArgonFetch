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

  // Whether the total came from the rendition rather than the response. A muxed stream sends no
  // length, but the server did predict one from the two tracks it is about to combine, so the
  // bar can be honest about roughly how far along it is instead of saying nothing at all.
  readonly isEstimatedTotal = signal(false);

  private lastTime = 0;
  private lastBytes = 0;
  private expectedBytes: number | null = null;

  constructor() {
    // A transfer lives in this page, so leaving takes it with you - reload included, which is
    // the easy one to do by accident. The browser will not let a page word its own warning, so
    // this only asks it to put its standard one up.
    window.addEventListener('beforeunload', event => {
      if (!this.isDownloading()) {
        return;
      }

      event.preventDefault();
      // Ignored by anything current, still required by older browsers to raise the prompt.
      event.returnValue = '';
    });
  }

  /** Whether a download is already running, which both callers refuse to interrupt. */
  get busy(): boolean {
    return this.isDownloading();
  }

  /**
   * @param expectedBytes Size the server predicted for this rendition, used only when the
   * response declares no length of its own. A prediction, so the bar it drives is one too.
   */
  download(url: string, fileName: string, expectedBytes?: number | null): Promise<void> {
    return new Promise<void>(resolve => {
      this.reset();
      this.isDownloading.set(true);
      this.fileName.set(fileName);
      this.downloadedMB.set('0');
      this.expectedBytes = expectedBytes && expectedBytes > 0 ? expectedBytes : null;
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
    // The combined endpoint muxes on the fly and cannot declare a length, so fall back to the
    // size the server predicted for the rendition. Only when neither exists does the bar go
    // indeterminate and leave the byte counter to carry the information.
    const estimated = !total && this.expectedBytes !== null;
    const denominator = total ?? this.expectedBytes;

    this.hasKnownTotal.set(!!denominator);
    this.isEstimatedTotal.set(estimated);

    if (denominator) {
      const percent = Math.round((loaded / denominator) * 100);

      // An estimate stops one short rather than sitting at 100% while bytes still arrive: a
      // mux weighs a little more than the two tracks going into it, so it will overshoot.
      this.progress.set(Math.max(0, Math.min(estimated ? 99 : 100, percent)));
      this.totalMB.set((denominator / 1024 / 1024).toFixed(1));
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
    this.isEstimatedTotal.set(false);
    this.expectedBytes = null;
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
