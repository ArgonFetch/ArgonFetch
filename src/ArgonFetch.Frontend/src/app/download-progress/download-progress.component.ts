import { Component, ChangeDetectionStrategy, inject } from '@angular/core';

import { MediaDownloadService } from '../services/media-download.service';

/**
 * The bar that shows how a download is going.
 * <p>
 * A component of its own because two views show it now - a single result and a playlist -
 * and it reads the shared service directly, so neither of them has to pass the values
 * through inputs.
 */
@Component({
  selector: 'app-download-progress',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.Eager,
  templateUrl: './download-progress.component.html',
  styleUrl: './download-progress.component.scss'
})
export class DownloadProgressComponent {
  private readonly downloads = inject(MediaDownloadService);

  // Signals, read straight from the service: HTTP progress events schedule no change
  // detection of their own in a zoneless application.
  readonly isDownloading = this.downloads.isDownloading;
  readonly progress = this.downloads.progress;
  readonly fileName = this.downloads.fileName;
  readonly speed = this.downloads.speed;
  readonly totalMB = this.downloads.totalMB;
  readonly downloadedMB = this.downloads.downloadedMB;
  readonly hasKnownTotal = this.downloads.hasKnownTotal;
}
