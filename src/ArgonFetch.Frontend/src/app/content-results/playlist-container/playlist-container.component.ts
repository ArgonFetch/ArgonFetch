import { Download, LoaderCircle, FileArchive } from 'lucide';
import { IconComponent } from '../../icon/icon.component';
import { Component, Input, ChangeDetectionStrategy, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { FetchService, MediaInformationDto, ResourceInformationDto } from '../../api';
import { MediaDownloadService } from '../../services/media-download.service';
import { NotificationService } from '../../notifications/notification.service';
import { ResourceUrlService } from '../../services/resource-url.service';
import { ArchiveDownloadService } from '../../services/archive-download.service';
import { DownloadProgressComponent } from '../../download-progress/download-progress.component';

@Component({
  selector: 'app-playlist-container',
  standalone: true,
  imports: [IconComponent, DownloadProgressComponent],
  templateUrl: './playlist-container.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './playlist-container.component.scss'
})
export class PlaylistContainerComponent {
  @Input() resourceInformation!: ResourceInformationDto;

  downloadIcon = Download;
  spinnerIcon = LoaderCircle;
  archiveIcon = FileArchive;

  private readonly downloads = inject(MediaDownloadService);
  private readonly fetchService = inject(FetchService);
  private readonly notifications = inject(NotificationService);
  private readonly resourceUrls = inject(ResourceUrlService);
  readonly archive = inject(ArchiveDownloadService);

  // Which row is currently being resolved. A playlist entry only carries a title and the
  // link it came from - the streams behind it are looked up when someone asks for them,
  // because resolving two thousand of them up front would take the better part of an hour.
  readonly resolving = signal<string | null>(null);

  // Mirrors the cap the archive endpoint enforces, so the button can say what it
  // will actually deliver rather than promising the whole list.
  private static readonly MaxArchiveTracks = 100;

  // The button still needs to know: a second transfer while one is running would
  // have nowhere to report itself.
  readonly isDownloading = this.downloads.isDownloading;


  /** Where the whole collection can be had as one zip, or null if we cannot address it. */
  archiveUrl(): string | null {
    return this.resourceUrls.buildArchiveUrl(this.resourceInformation.requestedUrl);
  }

  /**
   * What the zip button promises, which is not always the whole list: an archive carries a
   * fixed number of tracks, and a long playlist runs past it.
   */
  archiveHint(): string {
    const total = this.resourceInformation.mediaItems?.length ?? 0;

    return total > PlaylistContainerComponent.MaxArchiveTracks
      ? `Download the first ${PlaylistContainerComponent.MaxArchiveTracks} of ${total} tracks as a zip`
      : 'Download all tracks as a zip';
  }

  /**
   * Starts the archive and follows it.
   * <p>
   * The bytes go to the browser's own download manager, so this page is free to be left while
   * it runs; what is shown here is the server's account of how far it has got, which it can
   * give in tracks rather than in bytes it cannot total.
   */
  onDownloadArchive() {
    if (this.archive.isBuilding() || this.resolving()) {
      return;
    }

    const total = this.resourceInformation.mediaItems?.length ?? 0;
    const taking = Math.min(total, PlaylistContainerComponent.MaxArchiveTracks);

    if (taking < total) {
      this.notifications.show({
        title: 'Archiving the first ' + taking,
        message: `This collection has ${total} tracks and an archive carries ${taking}. Open the rest from the list to download them individually.`,
        tone: 'info'
      });
    }

    this.archive.start(this.resourceInformation.requestedUrl);
  }

  /** Whether this row is the one being worked on, so its button can show the wait. */
  isBusy(song: MediaInformationDto): boolean {
    return this.resolving() === song.requestedUrl;
  }

  /** Downloads one track of the playlist as audio, at the best quality on offer. */
  async onDownloadSong(song: MediaInformationDto) {
    // One at a time: the progress display has room for a single transfer, and a browser
    // would queue the rest anyway.
    if (this.downloads.busy || this.resolving() || !song.requestedUrl) {
      return;
    }

    const requestedUrl = song.requestedUrl;
    this.resolving.set(requestedUrl);

    try {
      const resolved = await firstValueFrom(this.fetchService.getResource(requestedUrl, 'body'));
      const media = resolved.mediaItems?.[0];
      const audio = media?.audio;

      // Renditions first, since they carry the real containers; the fixed rungs are what
      // sources that report no renditions still offer.
      const url = audio?.renditions?.length
        ? this.resourceUrls.buildRenditionUrl(audio.renditions[0])
        : this.resourceUrls.buildResourceUrl(audio, 'best');

      if (!url) {
        this.notifications.show({
          title: 'Nothing to download',
          message: `We couldn't find a playable source for "${song.title}".`,
          tone: 'error'
        });
        return;
      }

      const extension = audio?.renditions?.length
        ? audio.renditions[0].fileExtension || ''
        : this.resourceUrls.getFileExtension(audio, 'best');

      await this.downloads.download(url, `${media?.title || song.title}${extension}`);
    } catch (error: any) {
      // A Spotify track is played from somewhere else, and the odd remix or regional
      // release has no counterpart to find. That is a different thing from the source
      // being unreachable, and saying so saves someone retrying a track that will never
      // resolve.
      const missing = error?.status === 404;

      this.notifications.show({
        title: missing ? 'Nothing to download' : 'Fetch failed',
        message: missing
          ? `We couldn't find "${song.title}" anywhere we can download from.`
          : `We couldn't reach a source for "${song.title}". Please try again later.`,
        tone: 'error'
      });
    } finally {
      this.resolving.set(null);
    }
  }
}
