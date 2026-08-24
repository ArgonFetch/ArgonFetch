import { Download, ChevronRight, LoaderCircle } from 'lucide';
import { IconComponent } from '../../icon/icon.component';
import { Component, Input, ChangeDetectionStrategy, inject } from '@angular/core';
import { CdkMenu, CdkMenuItem, CdkMenuTrigger } from '@angular/cdk/menu';

import { MediaRenditionDto, ResourceInformationDto } from '../../api';
import { HttpClientModule } from '@angular/common/http';
import { MediaDownloadService } from '../../services/media-download.service';
import { ResourceUrlService } from '../../services/resource-url.service';
import { DownloadProgressComponent } from '../../download-progress/download-progress.component';

@Component({
  selector: 'app-single-song-container',
  standalone: true,
  imports: [IconComponent, HttpClientModule, CdkMenu, CdkMenuItem, CdkMenuTrigger, DownloadProgressComponent],
  templateUrl: './single-song-container.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './single-song-container.component.scss'
})
export class SingleSongContainerComponent {
  @Input() resourceInformation!: ResourceInformationDto;

  downloadIcon = Download;
  chevronIcon = ChevronRight;
  spinnerIcon = LoaderCircle;

  // The transfer lives in the shared service: a playlist row starts downloads too.
  private readonly downloads = inject(MediaDownloadService);

  // The button still needs to know: a second transfer while one is running would
  // have nowhere to report itself.
  readonly isDownloading = this.downloads.isDownloading;

  constructor(private resourceUrlService: ResourceUrlService) {}


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
    if (this.downloads.busy) {
      return;
    }

    const url = this.resourceUrlService.buildRenditionUrl(rendition);
    if (!url) {
      console.error('No URL available for the selected rendition');
      return;
    }

    const title = this.resourceInformation.mediaItems?.[0]?.title || 'download';
    const extension = rendition.fileExtension || '';

    await this.downloads.download(url, `${title}${extension}`);
  }

  async onDownload(quality: 'best' | 'medium' | 'worst', type: 'combined' | 'audio') {
    if (this.downloads.busy) {
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

    await this.downloads.download(url, `${filename}${extension}`);
  }

  hasCombinedUrls(): boolean {
    return !!this.resourceInformation.mediaItems?.[0]?.video;
  }

  hasAudioUrls(): boolean {
    return !!this.resourceInformation.mediaItems?.[0]?.audio;
  }

}
