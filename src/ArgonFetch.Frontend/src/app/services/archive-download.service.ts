import { Injectable, inject, signal } from '@angular/core';
import { environment } from '../../environments/environment';
import { ResourceUrlService } from './resource-url.service';

/**
 * Follows an archive the server is building.
 * <p>
 * The zip itself is fetched as a plain link, so the browser owns the transfer: it survives this
 * page being left or closed, and none of it is held in the tab. The cost is that the page cannot
 * see the download at all - so the server publishes progress against an id chosen here, and this
 * reads it back over a separate connection.
 * <p>
 * That gives better numbers than watching the bytes would have. The archive is written as it is
 * sent and so declares no length, so a byte counter would have had no total to measure against;
 * the server knows the track count before it starts.
 */
@Injectable({ providedIn: 'root' })
export class ArchiveDownloadService {
  private readonly resourceUrls = inject(ResourceUrlService);

  // Signals: written from EventSource callbacks, which schedule no change detection of their
  // own in a zoneless application.
  readonly isBuilding = signal(false);
  readonly total = signal(0);
  readonly completed = signal(0);
  readonly current = signal<string | null>(null);
  readonly skipped = signal(0);

  private source?: EventSource;

  /** Whole percent finished, for the bar. Zero until the server has reported a track count. */
  percent(): number {
    const total = this.total();

    return total > 0 ? Math.min(100, Math.round((this.completed() / total) * 100)) : 0;
  }

  /**
   * Starts the download and follows it. Resolves once the server says it is finished, or
   * immediately if there is nothing to download.
   */
  start(collectionUrl: string | null | undefined): void {
    if (this.isBuilding() || !collectionUrl) {
      return;
    }

    // Random rather than sequential: two people archiving at once must not be handed the same
    // id and watch each other's progress.
    const jobId = crypto.randomUUID();

    this.reset();
    this.isBuilding.set(true);

    this.watch(jobId);

    // Navigating an iframe rather than the page itself. A top-level navigation to a download
    // works, but it also counts as leaving, and some browsers tear down the EventSource before
    // the first event arrives.
    const frame = document.createElement('iframe');

    frame.hidden = true;
    frame.src = `${this.resourceUrls.buildArchiveUrl(collectionUrl)}&jobId=${encodeURIComponent(jobId)}`;
    document.body.appendChild(frame);

    // Removed once the response has certainly started; the download the browser has taken over
    // is not cancelled by the frame going away.
    window.setTimeout(() => frame.remove(), 120_000);
  }

  private watch(jobId: string) {
    this.source?.close();

    const source = new EventSource(`${environment.apiBaseUrl}/api/Stream/Archive/Progress/${encodeURIComponent(jobId)}`);
    this.source = source;

    source.onmessage = message => {
      const progress = JSON.parse(message.data) as {
        state: string; total: number; completed: number; current: string | null; skipped: number;
      };

      this.total.set(progress.total);
      this.completed.set(progress.completed);
      this.current.set(progress.current);
      this.skipped.set(progress.skipped);

      if (progress.state !== 'building') {
        this.finish();
      }
    };

    // Fired when the server closes the stream as well as on a real failure, and the two are not
    // distinguishable here. Either way there is nothing left to follow.
    source.onerror = () => this.finish();
  }

  private finish() {
    this.source?.close();
    this.source = undefined;
    this.isBuilding.set(false);
    this.current.set(null);
  }

  private reset() {
    this.total.set(0);
    this.completed.set(0);
    this.current.set(null);
    this.skipped.set(0);
  }
}
