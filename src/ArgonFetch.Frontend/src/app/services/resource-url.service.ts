import { Injectable } from '@angular/core';
import { environment } from '../../environments/environment';
import { MediaRenditionDto, StreamReferenceDto, UrlType } from '../api';

// StreamReferenceDto and UrlType come from the generated API client rather than being
// redefined here - the local copies had drifted from the generated ones, which is why
// callers had to cast through `any` to pass a DTO into this service.
export type { StreamReferenceDto, MediaRenditionDto };
export { UrlType };

@Injectable({
  providedIn: 'root'
})
export class ResourceUrlService {

  constructor() { }

  /**
   * Builds the URL for one rendition. A rendition the server marked as converted carries the
   * target container, which the stream endpoint takes as its format parameter.
   */
  buildRenditionUrl(rendition: MediaRenditionDto | null | undefined): string | null {
    if (!rendition?.key) {
      return null;
    }

    const endpoint = rendition.urlType === UrlType.Combined ? 'combined' : 'media';
    const url = `${environment.apiBaseUrl}/api/stream/${endpoint}/${rendition.key}`;

    return rendition.convertTo ? `${url}?format=${rendition.convertTo}` : url;
  }

  /**
   * The URL that serves a whole collection as one zip.
   * <p>
   * Takes the collection's own link rather than a cache key: the entries behind a listing are
   * deliberately left unresolved, so there are no keys to hand over yet - the server resolves
   * them as it writes the archive.
   */
  buildArchiveUrl(collectionUrl: string | null | undefined): string | null {
    if (!collectionUrl) {
      return null;
    }

    return `${environment.apiBaseUrl}/api/Stream/Archive?url=${encodeURIComponent(collectionUrl)}`;
  }

  /**
   * Human-readable transfer size, or an empty string when the source does not report one.
   * Shown next to a rendition because size is what tells someone how long it will take.
   */
  formatSize(bytes: number | null | undefined): string {
    if (!bytes || bytes <= 0) {
      return '';
    }

    const megabytes = bytes / (1024 * 1024);

    return megabytes >= 1024
      ? `${(megabytes / 1024).toFixed(1)} GB`
      : `${megabytes.toFixed(megabytes < 10 ? 1 : 0)} MB`;
  }

}
